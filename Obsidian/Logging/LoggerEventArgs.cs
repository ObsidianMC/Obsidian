using Obsidian.API.Events;

namespace Obsidian.Logging
{
    public class LoggerEventArgs : AsyncEventArgs
    {
        public LogMessage Message { get; }

        public LoggerEventArgs(LogMessage message) => this.Message = message;
    }
}
