using Microsoft.Extensions.Logging;
<<<<<<<< HEAD:Obsidian/Hosting/Logging/Logger.cs

namespace Obsidian.Hosting.Logging;
public class Logger : ILogger<Server>
========
using Obsidian.API.Utilities;
using System.Diagnostics;

namespace Obsidian.API.Logging;

public class Logger : ILogger
>>>>>>>> master:Obsidian.API/Logging/Logger.cs
{
    protected static readonly object _lock = new();

    private LogLevel MinimumLevel { get; }

    private string Prefix { get; }

    internal Logger(string prefix, LogLevel minLevel = LogLevel.Debug)
    {
        MinimumLevel = minLevel;
        Prefix = prefix;
    }

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
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
        }).ConsoleColor!.Value;

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

        var prefix = $"[{Prefix.Split('.')[^1]}] ";

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

        if (message.IsNullOrEmpty())
<<<<<<<< HEAD:Obsidian/Hosting/Logging/Logger.cs
        {
            this.LogTrace($"Empty log message sent. Dumping stacktrace:\n{new System.Diagnostics.StackTrace().ToString().Replace("\n", "            ")}");
========
        { 
            this.LogTrace($"Empty log message sent. Dumping stacktrace:\n{new StackTrace().ToString().Replace("\n", "            ")}");
>>>>>>>> master:Obsidian.API/Logging/Logger.cs
            return;
        }

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

<<<<<<<< HEAD:Obsidian/Hosting/Logging/Logger.cs
    public IDisposable BeginScope<TState>(TState state) where TState : notnull 
        => throw new NotImplementedException();
========
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
>>>>>>>> master:Obsidian.API/Logging/Logger.cs
}
