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

        private string Prefix;

        private bool _debug;

        internal Logger(string prefix)
        {
            this._messageLogged = new AsyncEvent<LoggerEventArgs>(LogError, "messagelogged");
            this.Prefix = prefix;
        }

        internal Logger(string prefix, Config config)
        {
            this._messageLogged = new AsyncEvent<LoggerEventArgs>(LogError, "messagelogged");
            this.Prefix = prefix;
            this._debug = config.DebugMode;
        }

        private void LogError(string eventname, Exception ex) { }

        public Task LogDebugAsync(string message)
        {
            if (!this._debug)
                return Task.CompletedTask;

            return this.LogMessageAsync(message, LogLevel.Debug);
        }
        public Task LogWarningAsync(string message) => this.LogMessageAsync(message, LogLevel.Warning);
        public Task LogErrorAsync(string message) => this.LogMessageAsync(message, LogLevel.Error);

        public async Task LogMessageAsync(string msg, LogLevel level = LogLevel.Info)
        {
            var datetime = DateTimeOffset.Now;
            await _messageLogged.InvokeAsync(new LoggerEventArgs(msg, Prefix, datetime));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[");
            Console.ResetColor();
            Console.Write("{0:t} " + level.ToString(), datetime);
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
            switch (level)
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

    public enum LogLevel
    {
        Info,

        Warning,

        Error,

        Debug
    }
}
