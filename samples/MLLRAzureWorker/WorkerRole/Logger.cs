namespace MLLR
{
    using System;
    using System.Diagnostics;
    using System.Diagnostics.Tracing;
    using Prajna.Tools;

    sealed class PrajnaEventSourceWriter : EventSource
    {
        public static PrajnaEventSourceWriter Logger = new PrajnaEventSourceWriter();
        public void Log(string Message) { if (IsEnabled()) WriteEvent(1, Message); }
    }


    class LoggerProvider : ILoggerProvider
    {
        public void Flush()
        {
            return;
        }

        public string GetArgsUsage()
        {
            return "";
        }

        public string GetLogFile()
        {
            throw new NotImplementedException();
        }

        public bool IsEnabled(Tuple<string, LogLevel> value)
        {
            return value.Item2 <= LogLevel.MildVerbose;
        }

        public bool IsEnabled(string _string, LogLevel _logLevel)
        {
            return IsEnabled(Tuple.Create(_string, _logLevel));
        }

        public void Log(Tuple<string, LogLevel, string> value)
        {
            var logId = value.Item1;
            var level = value.Item2;
            var msg = value.Item3;

            var sb = new System.Text.StringBuilder();

            sb.Append(System.Threading.Thread.CurrentThread.ManagedThreadId)
              .Append(",")
              .Append(level)
              .Append(",")
              .Append(msg);

            PrajnaEventSourceWriter.Logger.Log(sb.ToString());
        }

        public void Log(string _string1, LogLevel _logLevel, string _string2)
        {
            Log(Tuple.Create(_string1, _logLevel, _string2));
        }

        public void Log(Tuple<LogLevel, string> value)
        {
            Log(Tuple.Create("default", value.Item1, value.Item2));
        }

        public void Log(LogLevel _logLevel, string _string)
        {
            Log(Tuple.Create(_logLevel, _string));
        }

        public void ParseArgs(string[] value)
        {
            return;
        }
    }
}
