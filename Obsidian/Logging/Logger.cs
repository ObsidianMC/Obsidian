using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.Util.Extensions;
using System;
using System.Linq;

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

                #region Prefix
                var prefix = $"[{DateTimeOffset.Now:HH:mm:ss tt}] ";

                prefix += logLevel switch
                {
                    LogLevel.Trace => ChatColor.White,
                    LogLevel.Debug => ChatColor.Purple,
                    LogLevel.Information => ChatColor.Cyan,
                    LogLevel.Warning => ChatColor.Yellow,
                    LogLevel.Error => ChatColor.DarkRed,
                    LogLevel.Critical => ChatColor.Red,
                    _ => ChatColor.Gray,
                };

                prefix += logLevel switch
                {
                    LogLevel.Trace => "[Trace] ",
                    LogLevel.Debug => "[Debug] ",
                    LogLevel.Information => "[Info] ",
                    LogLevel.Warning => "[Warn] ",
                    LogLevel.Error => "[Error] ",
                    LogLevel.Critical => "[Crit] ",
                    LogLevel.None => "[None] ",
                    _ => "[????] "
                };
                prefix += ChatColor.Reset;

                // This is here because of weird formatting 
                if (this.Prefix.Split("/").Length > 0)
                {
                    if (logLevel == LogLevel.Debug || logLevel == LogLevel.Error)
                        prefix += $"{""}[{Prefix}] ";
                    else
                        prefix += $"{"",1}[{Prefix}] ";
                }
                else
                {
                    if (Prefix.Length >= 12)
                        prefix += $"{"",1}[{Prefix}] ";
                    else
                        prefix += $"[{Prefix}] ";
                }
                prefix.RenderColoredConsoleMessage();
                #endregion

                #region Message coloring & line break handling
                var message = formatter(state, exception);
                var msgLst = message.Contains("§") ? message.Split("§") : new string[] { $"f{message}" };
                if (msgLst.Length > 1 && msgLst[0].Length > 0 && msgLst[0][0] != '§') msgLst[0] = $"f{msgLst[0]}";
                foreach (var msg in msgLst)
                {
                    if (!string.IsNullOrEmpty(msg) && msg.Length > 1)
                    {
                        var colorStr = msg[0].ToString().ToLower()[0];

                        var color = ChatColor.FromCode(colorStr).ToConsoleColor();
                        string[] lines = msg.Contains("\n") ? msg.Split("\n").ToArray() : new string[] { msg };

                        for (int i = 0; i < lines.Length; i++)
                        {
                            if (i > 0) Console.WriteLine();
                            if (i > 0) { prefix.RenderColoredConsoleMessage(); }
                            $"§{(i > 0 ? $"{colorStr}" : "")}{lines[i]}".RenderColoredConsoleMessage();
                        }
                    }
                }
                Console.ResetColor();
                Console.WriteLine();
                #endregion

                if (exception != null)
                    Console.WriteLine(exception);
            }
        }

        public bool IsEnabled(LogLevel logLevel) => logLevel >= this.MinimumLevel;

        public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
    }
}
