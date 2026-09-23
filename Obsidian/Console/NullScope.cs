namespace Obsidian.Console;

/// <summary>No-op scope handed out when no <see cref="Microsoft.Extensions.Logging.IExternalScopeProvider"/> is set.</summary>
internal sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();

    private NullScope()
    {
    }

    public void Dispose()
    {
    }
}
