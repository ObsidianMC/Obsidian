using Microsoft.Extensions.Logging;
using System;

namespace Obsidian.Logging
{
    public class Logger : ILogger<Server>
    {
        private static readonly object _lock = new object();

        private LogLevel minimumLevel { get; }

        private string prefix { get; }

        internal Logger(string prefix, LogLevel minLevel = LogLevel.Debug)
        {
            this.minimumLevel = minLevel;
            this.prefix = prefix;
        }

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
        {
            if (!this.IsEnabled(logLevel))
                return;

            lock (_lock)
            {
                Console.Write($"[{DateTimeOffset.Now:HH:mm:ss tt}] ");


                Console.ForegroundColor = logLevel switch
                {
                    LogLevel.Trace => ConsoleColor.White,
                    LogLevel.Debug => ConsoleColor.DarkMagenta,
                    LogLevel.Information => ConsoleColor.Cyan,
                    LogLevel.Warning => ConsoleColor.Yellow,
                    LogLevel.Error => ConsoleColor.DarkRed,
                    LogLevel.Critical => ConsoleColor.Red,
                    _ => ConsoleColor.Gray,
                };

                Console.Write($"" + logLevel switch
                {
                    LogLevel.Trace => "[Trace] ",
                    LogLevel.Debug => "[Debug] ",
                    LogLevel.Information => "[Info] ",
                    LogLevel.Warning => "[Warn] ",
                    LogLevel.Error => "[Error] ",
                    LogLevel.Critical => "[Crit] ",
                    LogLevel.None => "[None] ",
                    _ => "????] "
                });
                Console.ResetColor();

                //This is here because of weird formatting 
                if (this.prefix.Split("/").Length > 0)
                {
                    if(logLevel == LogLevel.Debug || logLevel == LogLevel.Error)
                        Console.Write($"{""}[{prefix}] ");
                    else
                        Console.Write($"{"",1}[{prefix}] ");
                }
                else
                {
                    if (prefix.Length >= 12)
                        Console.Write($"{"", 1}[{prefix}] ");
                    else
                        Console.Write($"[{prefix}] ");
                }

                var message = formatter(state, exception);
                Console.WriteLine(message);

                if (exception != null)
                    Console.WriteLine(exception);
            }
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= this.minimumLevel;

        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
    }
}
