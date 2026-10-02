using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using Microsoft.Extensions.Options;

namespace Obsidian.Console;

/// <summary>Uses the standard formatters but writes each entry atomically with command editing.</summary>
[ProviderAlias("Console")]
public sealed partial class CoordinatedConsoleLoggerProvider(
    ConsoleTerminal terminal,
    IEnumerable<ConsoleFormatter> formatters,
    IOptionsMonitor<ConsoleLoggerOptions> options) : ILoggerProvider, ISupportExternalScope
{
    private readonly ConsoleTerminal terminal = terminal;

    private readonly IOptionsMonitor<ConsoleLoggerOptions> options = options;

    private readonly Dictionary<string, ConsoleFormatter> formatters =
        formatters.ToDictionary(formatter => formatter.Name, StringComparer.OrdinalIgnoreCase);

    private IExternalScopeProvider scopes = new LoggerExternalScopeProvider();

    public ILogger CreateLogger(string categoryName) => new Logger(this, categoryName);

    public void SetScopeProvider(IExternalScopeProvider scopeProvider) => this.scopes = scopeProvider;

    public void Dispose()
    {
    }
}
