using Microsoft.Extensions.Logging;
using Obsidian.Chat;
using System;

namespace Obsidian.Logging
{
    public class Logger : ILogger<Server>
    {
        protected static readonly object _lock = new object();

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
                    if (logLevel == LogLevel.Debug || logLevel == LogLevel.Error)
                        Console.Write($"{""}[{Prefix}] ");
                    else
                        Console.Write($"{"",1}[{Prefix}] ");
                }
                else
                {
                    if (Prefix.Length >= 12)
                        Console.Write($"{"",1}[{Prefix}] ");
                    else
                        Console.Write($"[{Prefix}] ");
                }

                var message = formatter(state, exception);
                var msgLst = message.Contains("§") ? message.Split("§") : new string[] { $"r{message}" };
                if (message[0] != '§' && msgLst.Length > 1) msgLst[0] = $"r{msgLst[0]}";
                foreach (var msg in msgLst)
                {
                    if (!string.IsNullOrEmpty(msg))
                    {
                        Console.ForegroundColor = msg[0].ToString().ToLower()[0] switch
                        {
                            '0' => ConsoleColor.Black,
                            '1' => ConsoleColor.DarkBlue,
                            '2' => ConsoleColor.DarkGreen,
                            '3' => ConsoleColor.DarkCyan,
                            '4' => ConsoleColor.DarkRed,
                            '5' => ConsoleColor.DarkMagenta,
                            '6' => ConsoleColor.DarkYellow,
                            '7' => ConsoleColor.Gray,
                            '8' => ConsoleColor.DarkGray,
                            '9' => ConsoleColor.Blue,
                            'a' => ConsoleColor.Green,
                            'b' => ConsoleColor.Cyan,
                            'c' => ConsoleColor.Red,
                            'd' => ConsoleColor.Magenta,
                            'e' => ConsoleColor.Yellow,
                            'f' => ConsoleColor.White,
                            'r' => ConsoleColor.White,
                            _ => ConsoleColor.White
                        };
                        Console.Write(msg.Substring(1));
                    }
                }
                Console.ResetColor();
                Console.WriteLine("");

                if (exception != null)
                    Console.WriteLine(exception);
            }
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= this.MinimumLevel;

        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
    }
}
