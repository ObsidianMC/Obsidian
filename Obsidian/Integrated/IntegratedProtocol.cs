using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Integrated;

/// <summary>
/// The control channel between an integrated server and its client: every control line, in both directions, is
/// <see cref="Prefix"/> followed by one compact camelCase JSON object. Commands come in on standard input, events go out
/// on standard output. See <c>docs/integrated-server.md</c>.
/// </summary>
public static class IntegratedProtocol
{
    public const string Prefix = "@obsidian:";

    private static readonly JsonSerializerOptions jsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    /// <summary>
    /// Whether <paramref name="line"/> is a control line, which other console command handlers mustn't see.
    /// </summary>
    public static bool IsControlLine(string line) => line.StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>
    /// Parses a control command line.
    /// </summary>
    /// <returns><c>false</c> when the line isn't a control line, or its JSON isn't a command.</returns>
    public static bool TryParseCommand(string line, [NotNullWhen(true)] out IntegratedCommand? command)
    {
        command = null;

        if (!IsControlLine(line))
            return false;

        try
        {
            command = JsonSerializer.Deserialize<IntegratedCommand>(line.AsSpan(Prefix.Length), jsonOptions);
        }
        catch (JsonException)
        {
            return false;
        }

        return !string.IsNullOrEmpty(command?.Command);
    }

    /// <summary>
    /// Formats an event as its control line, without the line break.
    /// </summary>
    public static string FormatEvent(IntegratedEvent integratedEvent) => Prefix + JsonSerializer.Serialize(integratedEvent, jsonOptions);
}

/// <summary>
/// A command from the client. Which fields matter depends on <see cref="Command"/>.
/// </summary>
public sealed record IntegratedCommand
{
    /// <summary>
    /// <c>pause</c>, <c>resume</c>, <c>save</c>, <c>stop</c> or <c>publish</c>.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// <c>save</c>: whether to flush to disk. Every save does, so it's accepted and ignored.
    /// </summary>
    public bool Flush { get; init; }

    /// <summary>
    /// <c>publish</c>: the LAN port; 0 or none picks a free one.
    /// </summary>
    public int? Port { get; init; }

    /// <summary>
    /// <c>publish</c>: the game mode forced on players who join from now on (vanilla's id, e.g. <c>survival</c>); none
    /// keeps the world's default.
    /// </summary>
    public string? GameMode { get; init; }

    /// <summary>
    /// <c>publish</c>: whether every player may use commands from now on.
    /// </summary>
    public bool AllowCommands { get; init; }
}

/// <summary>
/// An event for the client; fields that don't apply are left out.
/// </summary>
public sealed record IntegratedEvent(string Event)
{
    public int? Port { get; init; }
    public string? Stage { get; init; }
    public int? Percent { get; init; }
    public bool? Autosave { get; init; }
    public double? TickMs { get; init; }
    public string? Message { get; init; }

    public static IntegratedEvent Ready(int port) => new("ready") { Port = port };
    public static IntegratedEvent Progress(string stage, int percent) => new("progress") { Stage = stage, Percent = percent };
    public static IntegratedEvent Saving(bool autosave) => new("saving") { Autosave = autosave };
    public static IntegratedEvent Saved(bool autosave) => new("saved") { Autosave = autosave };
    public static IntegratedEvent SaveFailed(string message) => new("saveFailed") { Message = message };
    public static IntegratedEvent Paused() => new("paused");
    public static IntegratedEvent Resumed() => new("resumed");
    public static IntegratedEvent Published(int port) => new("published") { Port = port };
    public static IntegratedEvent PublishFailed(string message) => new("publishFailed") { Message = message };
    public static IntegratedEvent Stats(double tickMs) => new("stats") { TickMs = Math.Round(tickMs, 1) };
    public static IntegratedEvent Stopping() => new("stopping");
    public static IntegratedEvent Crashed(string message) => new("crashed") { Message = message };
}
