using Obsidian.Entities;
using Obsidian.Events;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Obsidian.Logging
{
    public class Logger
    {
        public event AsyncEventHandler<LoggerEventArgs> MessageLogged
        {
            add { this._messageLogged.Register(value); }
            remove { this._messageLogged.Unregister(value); }
        }

        private readonly AsyncEvent<LoggerEventArgs> _messageLogged;

        public LogLevel LogLevel { get; set; }

        private readonly ConcurrentQueue<LogMessage> _messages = new ConcurrentQueue<LogMessage>();

        private readonly string Prefix;

        internal Logger(string prefix, LogLevel logLevel)
        {
            this._messageLogged = new AsyncEvent<LoggerEventArgs>(LogError, "messagelogged");
            this.Prefix = prefix;
            this.LogLevel = logLevel;

            Task.Run(async delegate () //HACK: Please oversee this, and do any changes if necessary.
            {
                while (true)
                {
                    while (_messages.TryDequeue(out LogMessage message))
                    {
                        await LogMessageAsync(message.Message, message.Level, message.DateTime);
                    }
                    await Task.Delay(100);
                }
            });
        }

        private void LogError(string eventname, Exception ex)
        {
        }

        public void LogDebug(string message) => this.LogMessage(message, LogLevel.Debug);

        public void LogWarning(string message) => this.LogMessage(message, LogLevel.Warning);

        public void LogError(string message) => this.LogMessage(message, LogLevel.Error);

        public void LogMessage(string msg, LogLevel logLevel = LogLevel.Info) => _messages.Enqueue(new LogMessage(msg, logLevel, DateTimeOffset.Now));

        private async Task LogMessageAsync(string msg, LogLevel logLevel, DateTimeOffset dateTime)
        {
            await _messageLogged.InvokeAsync(new LoggerEventArgs(msg, Prefix, dateTime));

            //checking if message should be printed or not
            if (logLevel > LogLevel)
            {
                return;
            }

            string line = "";

            line += string.Format("[{0:t}] " + logLevel.ToString() + " ", dateTime);

            if (Prefix != "")
            {
                line += $"[{Prefix}] ";
            }

            line += msg;

            Console.ForegroundColor = GetConsoleColor(logLevel);
            Console.WriteLine(line);
        }

        private static ConsoleColor GetConsoleColor(LogLevel logLevel)
        {
            switch (logLevel)
            {
                case LogLevel.Info: return ConsoleColor.Cyan;
                case LogLevel.Warning: return ConsoleColor.Yellow;
                case LogLevel.Error: return ConsoleColor.DarkRed;
                case LogLevel.Debug: return ConsoleColor.Magenta;
                default: return ConsoleColor.Gray;
            }
        }
    }

    public struct LogMessage
    {
        public LogLevel Level;
        public DateTimeOffset DateTime;
        public string Message;

        public LogMessage(string message, LogLevel level, DateTimeOffset dateTime)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Level = level;
            DateTime = dateTime;
        }
    }
}