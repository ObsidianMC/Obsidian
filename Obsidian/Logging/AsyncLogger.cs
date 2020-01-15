using Obsidian.Events;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Obsidian.Logging
{
    public class AsyncLogger
    {
        private readonly AsyncEvent<LoggerEventArgs> _messageLogged;
        private readonly ConcurrentQueue<LogMessage> _messages = new ConcurrentQueue<LogMessage>();
        private readonly Task _dispatcherTask;

        private bool _running = true;

        public string Prefix { get; }

        public event AsyncEventHandler<LoggerEventArgs> MessageLogged
        {
            add { this._messageLogged.Register(value); }
            remove { this._messageLogged.Unregister(value); }
        }

        public LogLevel LogLevel { get; set; }

        internal AsyncLogger(string prefix, LogLevel logLevel)
        {
            this._messageLogged = new AsyncEvent<LoggerEventArgs>(LogError, "messagelogged");
            this.Prefix = prefix;
            this.LogLevel = logLevel;

            _dispatcherTask = Task.Run(TaskLoop);
        }
        
        public void StopLogging() => this._running = false;


        private async Task TaskLoop()
        {
            while (_running)
            {
                if (_messages.TryDequeue(out LogMessage message))
                {
                    await LogMessageAsync(message.Message, message.Level);
                }
                await Task.Delay(100);
            }
        }

        private void LogError(string eventname, Exception ex)
        {
        }

        public void LogDebug(string message) => this.LogMessage(message, LogLevel.Debug);

        public void LogWarning(string message) => this.LogMessage(message, LogLevel.Warning);

        public void LogError(string message) => this.LogMessage(message, LogLevel.Error);

        public void LogMessage(string message, LogLevel logLevel = LogLevel.Info) => _messages.Enqueue(new LogMessage(message, logLevel));

        public async Task LogMessageAsync(string message, LogLevel logLevel, ConsoleColor color)
        {
            await _messageLogged.InvokeAsync(new LoggerEventArgs(message, Prefix, DateTimeOffset.Now));

            //checking if message should be printed or not
            if (logLevel > LogLevel)
            {
                return;
            }

            string line = "";

            line += string.Format("[{0:t}] " + logLevel.ToString() + " ", DateTimeOffset.Now);

            if (Prefix != "")
            {
                line += $"[{Prefix}] ";
            }

            line += message;

            Console.ForegroundColor = color;
            Console.WriteLine(line);
        }

        private async Task LogMessageAsync(string message, LogLevel logLevel)
        {
            await _messageLogged.InvokeAsync(new LoggerEventArgs(message, Prefix, DateTimeOffset.Now));

            //checking if message should be printed or not
            if (logLevel > LogLevel)
            {
                return;
            }

            string line = "";

            line += string.Format("[{0:t}] " + logLevel.ToString() + " ", DateTimeOffset.Now);

            if (Prefix != "")
            {
                line += $"[{Prefix}] ";
            }

            line += message;

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

        public LogMessage(string message, LogLevel level)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Level = level;
            DateTime = DateTimeOffset.Now;
        }
    }
}