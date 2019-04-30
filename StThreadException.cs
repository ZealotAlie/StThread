using System;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

public class StThreadException : Exception
{
    string          myCallStack;
    string          myMessage;

    public StThreadException(Exception e,string callStack)
    {
        Regex regExMoveNext = new Regex(@"\+\<(.+)\>(.+)\(\)");
        Regex regStThread = new Regex(@"at StThread\.");

        myMessage = e.Message;
        string[] frames = e.StackTrace.Split('\n');
        foreach(string f in frames)
        {
            var match = regExMoveNext.Match(f);
            if(match.Success)
            {
                var token = match.Groups[0];
                myCallStack += f.Substring(0, token.Index) + '.' + match.Groups[1].Value 
                    + " ()" + f.Substring(token.Index + token.Length) + '\n';
            }
            else
            {
                match = regStThread.Match(f);
                if(!match.Success)
                {
                    myCallStack += f + '\n';
                }
            }
        }
        myCallStack += callStack;
    }

    public override string ToString()
    {
        return Message;
    }
    public override string Message
    {
        get
        {
            return myMessage;
        }
    }
    public override string StackTrace
    {
        get
        {
            return myCallStack;
        }
    }
}
