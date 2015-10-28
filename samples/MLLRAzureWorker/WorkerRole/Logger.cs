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

        public void Log(Tuple<LogLevel, string> value)
        {
            Log(Tuple.Create("default", value.Item1, value.Item2));
        }

        public void ParseArgs(string[] value)
        {
            return;
        }
    }
}
