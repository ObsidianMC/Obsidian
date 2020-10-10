using Microsoft.Extensions.Logging;
using System;

namespace Obsidian.Logging
{
    public class Logger : ILogger<Server>
    {
        private static readonly object _lock = new object();

        private LogLevel MinimumLevel { get; }

        private string Prefix { get; }

        internal Logger(string prefix, LogLevel minLevel = LogLevel.Debug)
        {
            this.MinimumLevel = minLevel;
            this.Prefix = prefix;
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

                Console.Write(logLevel switch
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

                // This is here because of weird formatting 
                if (this.Prefix.Split("/").Length > 0)
                {
                    if(logLevel == LogLevel.Debug || logLevel == LogLevel.Error)
                        Console.Write($"{""}[{Prefix}] ");
                    else
                        Console.Write($"{"",1}[{Prefix}] ");
                }
                else
                {
                    if (Prefix.Length >= 12)
                        Console.Write($"{"", 1}[{Prefix}] ");
                    else
                        Console.Write($"[{Prefix}] ");
                }

                var message = formatter(state, exception);
                Console.WriteLine(message);

                if (exception != null)
                    Console.WriteLine(exception);
            }
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= this.MinimumLevel;

        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
    }
}
