using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using UnityEngine;

public class StThread
{
    public enum EState
    {
        None,
        Running,
        Background,
        Finished,
    };
#if DEBUG
    public string CallStack
    {
        get
        {
            mCallStackBuilder.Length = 0;
            foreach(var s in mCallStackFrames)
            {
                mCallStackBuilder.Append(s);
                mCallStackBuilder.Append('\n');
            }
            return mCallStackBuilder.ToString();
        }
    }
#endif

    public static StThread          RunningThread
    {
        get;
        private set;
    }

    EState                          mState;
    Stack<IEnumerator>              mContextStack;
#if DEBUG
    Stack<string>                   mCallStackFrames;
    StringBuilder                   mCallStackBuilder;
#endif


    public StThread()
    {
        mContextStack       = new Stack<IEnumerator>();
#if DEBUG
        mCallStackFrames    = new Stack<string>();
        mCallStackBuilder   = new StringBuilder();
#endif
    }
    public IEnumerator RunThread(IEnumerator context)
    {
        ThreadBegin(context);

        while(true)
        {
            if(InternalRun())
            {
                break;
            }

            yield return null;
        }

        ThreadEnd();
    }
    public virtual void Reset()
    {
        Debug.Assert(IsFinished(), "Try to reset unfinished thread.");
        mState = EState.None;
    }

    public bool IsFinished()
    {
        return mState == EState.Finished;
    }
    public bool IsInBackground()
    {
        return mState == EState.Background;
    }

    public EState GetState()
    {
        return mState;
    }

    protected void SetState(EState newState)
    {
#if DEBUG
        Debug.AssertFormat(mStateLockReason == null, "State can't be changed because {0}", mStateLockReason);
#endif
        mState = newState;
    }

    protected virtual bool InternalRun()
    {
        RunningThread = this;
        bool isDone = true;
        while(mContextStack.Count > 0)
        {
            IEnumerator top = mContextStack.Peek();
#if DEBUG
            try
            {
#endif
                while(isDone = !top.MoveNext())
                {
                    //If one stack frame done, we will try to make it run 
                    ContextStackPop();
                    if(mContextStack.Count > 0)
                    {
                        top = mContextStack.Peek();
                    }
                    else
                    {
                        break;
                    }
                }
#if DEBUG
            }
            catch(Exception e)
            {
                throw new StThreadException(e, CallStack);
            }
#endif

            if(mContextStack.Count > 0 && top.Current != null)
            {
#if DEBUG
                Debug.Assert(((IEnumerator)top.Current).Current == null, "New Stack Frame run before push To Context Stack.");
#endif
                ContextStackPush((IEnumerator)top.Current);
                //Force run again to avoid yield return CallFunction one frame delay problem. 
                continue;
            }

            break;
        }

        RunningThread = null;
        return isDone;
    }
    protected virtual void ThreadBegin(IEnumerator context)
    {
        Debug.Assert(mState == EState.None, "Try to run uninitialized thread.");
        mContextStack.Clear();
        ContextStackPush(context);
        mState = EState.Running;
    }
    protected void ThreadEnd()
    {
        mState = EState.Finished;
        Debug.Assert(mContextStack.Count == 0, "Not all stack frame finished.");
    }

    protected void ContextStackPush(IEnumerator context)
    {
        mContextStack.Push(context);
#if DEBUG
        Debug.AssertFormat(mContextStack.Count == mCallStackFrames.Count + 1,
                   "Forget TrackStack when yield return? Next Call is {0}", context.ToString());
#endif
    }
    protected IEnumerator ContextStackPop()
    {
#if DEBUG
        Debug.Assert(mContextStack.Count == mCallStackFrames.Count + 1, "Please use ContextStackPush and ContextStackPop.");
        if(mContextStack.Count > 1)
        {
            mCallStackFrames.Pop();
        }
#endif
        return mContextStack.Pop();
    }

#if DEBUG
    private string mStateLockReason;
    protected void LockState(string reason)
    {
        mStateLockReason = reason;
    }
    protected void UnlockState()
    {
        mStateLockReason = null;
    }
    public void TraceStack(System.Diagnostics.StackFrame stackFrame)
    {
        var method = stackFrame.GetMethod();
        string className = method.DeclaringType.Name;
        string methodName = method.Name;
        Regex regEx = new Regex(@"(.+)\+\<(.+)\>");
        var match = regEx.Match(method.DeclaringType.ToString());
        if(match.Success)
        {
            className = match.Groups[1].Value;
            methodName = match.Groups[2].Value;
        }

        mCallStackBuilder.Length = 0;
        mCallStackBuilder.AppendFormat
            (
            "at {0}.{1} () [0x0000] in {2}:{3}",
            className,
            methodName,
            stackFrame.GetFileName(),
            stackFrame.GetFileLineNumber()
            );

        mCallStackFrames.Push(mCallStackBuilder.ToString());
    }
#endif
}

public class StClusterThread : StThread
{
    List<StThread>      mChildThread;
    List<IEnumerator>   mChildContext;
    int                 mRunChildThreadIndex;
    int                 mThisFrameChildNum;

    bool                mWaitChild;

    public StClusterThread()
    {
        mChildThread    = new List<StThread>();
        mChildContext   = new List<IEnumerator>();
        mWaitChild      = true;
    }

    public void AddChildThread(StThread child, IEnumerator context)
    {
        mChildThread.Add(child);
        mChildContext.Add(child.RunThread(context));
    }

    protected override bool InternalRun()
    {
#if DEBUG
        LockState("Parent's state should never changed in child threads.");
#endif
        mThisFrameChildNum = mChildThread.Count;
        for(mRunChildThreadIndex = 0; mRunChildThreadIndex < mThisFrameChildNum; ++mRunChildThreadIndex)
        {
            if(mChildThread[mRunChildThreadIndex].IsFinished())
            {
                continue;
            }
            mChildContext[mRunChildThreadIndex].MoveNext();
        }
#if DEBUG
        UnlockState();
#endif

        bool isDone = base.InternalRun();

        if(isDone && mWaitChild)
        {
            foreach(var t in mChildThread)
            {
                if(!t.IsFinished())
                {
                    return false;
                }
            }
        }

        return isDone;
    }
    public override void Reset()
    {
        base.Reset();
        mChildThread.Clear();
        mChildContext.Clear();
    }
}