using Obsidian.Events;
using System;
using System.Collections.Generic;
using System.Text;

namespace Obsidian.Logging
{
    public class LoggerEventArgs : AsyncEventArgs
    {
        public string Message { get; private set; }
        public string Application { get; private set; }
        public DateTimeOffset DateTime { get; private set; }

        public LoggerEventArgs(string message, string application, DateTimeOffset datetime)
        {
            this.Message = message;
            this.DateTime = datetime;
            this.Application = application;
        }
    }
}
