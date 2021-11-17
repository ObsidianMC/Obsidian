using Microsoft.Extensions.Logging;
using Obsidian.IO.Console;

namespace Obsidian.Logging;

public class Logger : ILogger<Server>
{
    protected static readonly object _lock = new();

    private LogLevel MinimumLevel { get; }

    private string Prefix { get; }

    internal Logger(string prefix, LogLevel minLevel = LogLevel.Debug)
    {
        MinimumLevel = minLevel;
        Prefix = prefix;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception exception, Func<TState, Exception, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        string time = $"[{DateTimeOffset.Now:HH:mm:ss}] ";

        ConsoleColor logLevelColor = (logLevel switch
        {
            LogLevel.Trace => ChatColor.White,
            LogLevel.Debug => ChatColor.Purple,
            LogLevel.Information => ChatColor.Cyan,
            LogLevel.Warning => ChatColor.Yellow,
            LogLevel.Error => ChatColor.DarkRed,
            LogLevel.Critical => ChatColor.Red,
            _ => ChatColor.Gray,
        }).ConsoleColor.Value;

        string level = logLevel switch
        {
            LogLevel.Trace => "[Trace] ",
            LogLevel.Debug => "[Debug] ",
            LogLevel.Information => "[Info]  ",
            LogLevel.Warning => "[Warn]  ",
            LogLevel.Error => "[Error] ",
            LogLevel.Critical => "[Crit]  ",
            LogLevel.None => "[None]  ",
            _ => "[????]  "
        };

        string prefix = $"[{Prefix}] ";

        void PrintLinePrefix()
        {
            CommandLine.ResetColor();
            CommandLine.Write(time);
            CommandLine.ForegroundColor = logLevelColor;
            CommandLine.Write(level);
            CommandLine.ResetColor();
            CommandLine.Write(prefix);
        }

        string message = formatter(state, exception);
        string[] lines = message.Split('\n');

        lock (_lock)
        {
            for (int i = 0; i < lines.Length; i++)
            {
                PrintLinePrefix();

                CommandLine.ForegroundColor = ConsoleColor.White;
                lines[i].RenderColoredConsoleMessage();
                CommandLine.WriteLine();
            }
        }
        }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

    public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
}
