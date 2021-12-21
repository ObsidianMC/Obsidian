using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.ConsoleApp.IO;

namespace Obsidian.ConsoleApp;

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

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
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
        }).ConsoleColor.GetValueOrDefault(ConsoleColor.Gray);

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
                RenderColoredConsoleMessage(lines[i]);
                CommandLine.WriteLine();
            }
        }
    }

    public bool IsEnabled(LogLevel logLevel) => logLevel >= MinimumLevel;

    public IDisposable BeginScope<TState>(TState state) => throw new NotSupportedException();

    private static void RenderColoredConsoleMessage(string message)
    {
        int start = 0;
        int end = message.Length - 1;

        for (int i = 0; i < end; i++)
        {
            if (message[i] != '&' && message[i] != '§')
                continue;

            // Validate color code
            char colorCode = message[i + 1];
            if (!ChatColor.TryParse(colorCode, out var color))
                continue;

            // Print text with previous color
            if (start != i)
            {
                CommandLine.Write(message.AsSpan(start, i - start));
            }

            // Change color
            if (colorCode == 'r')
            {
                CommandLine.ResetColor();
            }
            else
            {
                if (color.ConsoleColor.HasValue)
                    CommandLine.ForegroundColor = color.ConsoleColor.Value;
            }

            // Skip color code
            i++;
            start = i + 1;
        }

        // Print remaining text if any
        if (start != message.Length)
            CommandLine.Write(message.AsSpan(start));

        CommandLine.ResetColor();
    }
}
