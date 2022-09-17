using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.Utilities;

namespace Obsidian.ConsoleApp.Logging;

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

        var time = $"[{DateTimeOffset.Now:HH:mm:ss}] ";

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

        var level = logLevel switch
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

        var prefix = $"[{Prefix}] ";

        void PrintLinePrefix()
        {
            Console.ResetColor();
            ConsoleIO.Write(time);
            Console.ForegroundColor = logLevelColor;
            ConsoleIO.Write(level);
            Console.ResetColor();
            ConsoleIO.Write(prefix);
        }

        var message = formatter(state, exception);
        var lines = message.Split('\n');

        lock (_lock)
            for (var i = 0; i < lines.Length; i++)
            {
                PrintLinePrefix();

                Console.ForegroundColor = ConsoleColor.White;
                lines[i].RenderColoredConsoleMessage();
                ConsoleIO.WriteLine(string.Empty);
            }
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

    public IDisposable BeginScope<TState>(TState state) => throw new NotImplementedException();
}
