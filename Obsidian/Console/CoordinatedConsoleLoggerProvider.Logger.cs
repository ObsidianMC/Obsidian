using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging.Console;
using System.IO;

namespace Obsidian.Console;

public sealed partial class CoordinatedConsoleLoggerProvider
{
    private sealed class Logger(CoordinatedConsoleLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => provider.scopes.Push(state);

        public bool IsEnabled(LogLevel logLevel) => logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!this.IsEnabled(logLevel))
                return;

            var entry = new LogEntry<TState>(logLevel, category, eventId, state, exception, formatter);
            string name = provider.options.CurrentValue.FormatterName ?? ConsoleFormatterNames.Simple;

            if (!provider.formatters.TryGetValue(name, out ConsoleFormatter? consoleFormatter))
                consoleFormatter = provider.formatters[ConsoleFormatterNames.Simple];

            using var writer = new StringWriter();

            consoleFormatter.Write(in entry, provider.scopes, writer);

            string message = writer.ToString();

            if (message.Length != 0)
                provider.terminal.WriteLog(message,
                    logLevel >= provider.options.CurrentValue.LogToStandardErrorThreshold);
        }
    }
}
