#define TRACE
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

//Added
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.ComponentModel;
using System.Configuration;
using System.Xml;
using System.Linq.Expressions;
using System.Runtime.InteropServices;
using System.Diagnostics.Tracing;
using System.IO;
//using System.Diagnostics.Tracing.EventSource;   // Version 4.5 of .Net

namespace Logger
{
    public static class MemberInfoGetting
    {
        public static string GetMemberName<T>(Expression<Func<T>> memberExpression)
        {
            MemberExpression expressionBody = (MemberExpression)memberExpression.Body;
            return expressionBody.Member.Name;
        }
    }

    public sealed class LogEventSource : EventSource
    {
        public void Load(string message)
        {
            WriteEvent(1, message);
        }

        public static LogEventSource etwLog = new LogEventSource();
    }


    public static class Log
    {
        //TraceEvent requires an id number, I just count the number of entries with this variable
        //I also use this to see if I need to initialize the config File info
        private static int iCnt = 1;

        public enum Nav
        {
            NavIn,
            NavOut,
            NavNone
        }

        public enum LogType
        {
            Error,
            Info,
            Warning
        }

        public static void TraceMessage<T>(Expression<Func<T>> expression)
        {
            MemberExpression expressionBody = (MemberExpression)expression.Body;
            string oldName = expressionBody.Member.Name;
            object value = expression.Compile().Invoke();
            TraceMessage(Nav.NavNone, oldName + "=" + value, LogType.Info);
        }

        public static void TraceMessage(Nav functionNav, string message, LogType type, [CallerMemberName] string callingMethod = "", [CallerFilePath] string callingFilePath = "", [CallerLineNumber] int callingFileLineNumber = 0)
        {
            if (type == LogType.Error)
            {
                Error(functionNav, message, callingFilePath, callingFileLineNumber, callingMethod);
            }
            if (type == LogType.Info)
            {
                Info(functionNav, message, callingFilePath, callingFileLineNumber, callingMethod);
            }
            if (type == LogType.Warning)
            {
                Warning(functionNav, message, callingFilePath, callingFileLineNumber, callingMethod);
            }
        }


        public static void TraceMessage(Nav functionNav, Exception ex, [CallerMemberName] string callingMethod = null, [CallerFilePath] string callingFilePath = null, [CallerLineNumber] int callingFileLineNumber = 0)
        {

            Error(functionNav, ex.Message, callingFilePath, callingFileLineNumber, callingMethod);

        }


        // Example:  2015-07-21 12:23:17, Error, LPR, Start
        public static void Error(Nav functionNav, string message, string path, int line, string method)
        {
            WriteEntry(TraceEventType.Error, functionNav, message, path, line, method);
        }

        // Example:  2015-07-21 12:23:17, Warn, LPR, Start
        public static void Warning(Nav functionNav, string message, string path, int line, string method)
        {
            WriteEntry(TraceEventType.Warning, functionNav, message, path, line, method);
        }

        // Example:  2015-07-21 12:23:17, Info, LPR, Start
        public static void Info(Nav functionNav, string message, string path, int line, string method)
        {
            WriteEntry(TraceEventType.Information, functionNav, message, path, line, method);
        }

        private static void WriteEntry(TraceEventType type, Nav functionNav, string message, string filePath, int lineNumber, string methodName)
        {
            string NavSymbol;

            if (iCnt == 1)
            {
                Stream myFile = File.Create("Log.txt");
                Trace.AutoFlush = true;
                Trace.Listeners.Add(new TextWriterTraceListener(myFile));
            }
            if (functionNav == Nav.NavIn)
                NavSymbol = "-->";
            else if (functionNav == Nav.NavOut)
                NavSymbol = "<--";
            else
                NavSymbol = "++>";      //NavNone

            methodName = NavSymbol + methodName;

            int PathBreakPoint = filePath.LastIndexOf(@"\");
            string fileName = filePath.Substring(PathBreakPoint + 1);


            //We calculate some padding so the log file columns line up
            int DatePad = 0;
            if (type == TraceEventType.Error)
                DatePad = DatePad + 6;
            else if (type == TraceEventType.Warning)
                DatePad = DatePad + 4;

            string padding = new string(' ', 25);

            if (iCnt < 10)
                DatePad = DatePad + 2;
            else if (iCnt < 100)
                DatePad = DatePad + 1;

            int FilePad = 20 - fileName.Length;
            if (FilePad < 0)
                FilePad = 0;

            int MethPad = 20 - methodName.Length;
            if (MethPad < 0)
                MethPad = 0;

            string Message = string.Format("{0}|{1}|{2}{3}[{4}]|{5}{6}|{7}",
                                  padding.Substring(0, DatePad),
                                  DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                                  padding.Substring(0, FilePad),
                                  fileName,
                                  lineNumber.ToString("D4"),
                                  methodName,
                                  padding.Substring(0, MethPad),
                                  message);

            try
            {
                Trace.WriteLine(type + " " + iCnt + Message);
            }
            catch (Exception e)
            {
                Console.WriteLine(String.Format("Logger exception: {0}", e.ToString()));
            }
            iCnt++;

        }
    }
}
