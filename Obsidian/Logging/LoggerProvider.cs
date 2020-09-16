using Microsoft.Extensions.Logging;
using System;
using System.Collections.Concurrent;

namespace Obsidian.Logging
{
    public class LoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentDictionary<string, Logger> loggers = new ConcurrentDictionary<string, Logger>();

        private LogLevel MinimumLevel { get; }

        private bool disposed = false;

        internal LoggerProvider(LogLevel minLevel = LogLevel.Information)
        {
            this.MinimumLevel = minLevel;
        }

        public ILogger CreateLogger(string categoryName)
        {
            if (this.disposed)
                throw new InvalidOperationException("This logger provider is already disposed.");


            return this.loggers.GetOrAdd(categoryName, name => new Logger(name, this.MinimumLevel));
        }

        public void Dispose()
        {
            this.loggers.Clear();
            this.disposed = true;
        }
    }
}
