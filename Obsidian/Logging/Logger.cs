using Obsidian.Events;
using System;
using System.Collections.Generic;
using System.Text;
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

        internal Logger(string prefix)
        {
            this._messageLogged = new AsyncEvent<LoggerEventArgs>(LogError, "messagelogged");
            this.Prefix = prefix;
        }

        private void LogError(string eventname, Exception ex)
        {

        }

        public async Task LogMessageAsync(string msg)
        {
            var datetime = DateTimeOffset.Now;
            await _messageLogged.InvokeAsync(new LoggerEventArgs(msg, Prefix, datetime));
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("[");
            Console.ResetColor();
            Console.Write(datetime.ToString());
            Console.ForegroundColor = ConsoleColor.Green;
            Console.Write("] ");
            
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
            Console.WriteLine(msg);
        }
    }
}
