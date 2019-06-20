using Obsidian.Entities;
using Obsidian.Events;
using System;
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

        private AsyncEvent<LoggerEventArgs> _messageLogged;

        public LogLevel LogLevel { get; set; }

        private string Prefix;

        internal Logger(string prefix, LogLevel logLevel)
        {
            this._messageLogged = new AsyncEvent<LoggerEventArgs>(LogError, "messagelogged");
            this.Prefix = prefix;
            this.LogLevel = logLevel;
        }

        private void LogError(string eventname, Exception ex)
        {
        }

        public Task LogDebugAsync(string message) => this.LogMessageAsync(message, LogLevel.Debug);

        public Task LogWarningAsync(string message) => this.LogMessageAsync(message, LogLevel.Warning);

        public Task LogErrorAsync(string message) => this.LogMessageAsync(message, LogLevel.Error);

        public async Task LogMessageAsync(string msg, LogLevel logLevel = LogLevel.Info)
        {
            var datetime = DateTimeOffset.Now;
            await _messageLogged.InvokeAsync(new LoggerEventArgs(msg, Prefix, datetime));

            //checking if message should be printed or not
            if (logLevel > LogLevel)
            {
                return;
            }

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[");

            Console.ResetColor();

            Console.Write("{0:t} " + logLevel.ToString(), datetime);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("]");

            if (Prefix != "")
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write("[");

                Console.ResetColor();
                Console.Write(Prefix);

                Console.ForegroundColor = ConsoleColor.Blue;
                Console.Write("] ");
            }

            Console.ResetColor();

            switch (logLevel)
            {
                case LogLevel.Warning:
                    Console.ForegroundColor = ConsoleColor.Yellow;
                    break;

                case LogLevel.Error:
                    Console.ForegroundColor = ConsoleColor.DarkRed;
                    break;

                case LogLevel.Debug:
                    Console.ForegroundColor = ConsoleColor.Magenta;
                    break;
            }

            Console.WriteLine(msg);
        }
    }
}