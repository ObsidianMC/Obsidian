using System;

namespace Obsidian.Logging
{
    public struct LogMessage
    {
        public readonly LogLevel Level;
        public readonly DateTimeOffset DateTime;
        public readonly string Message;

        public LogMessage(string message, LogLevel level)
        {
            Message = message ?? throw new ArgumentNullException(nameof(message));
            Level = level;
            DateTime = DateTimeOffset.Now;
        }
    }
}