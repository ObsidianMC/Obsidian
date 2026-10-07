namespace Obsidian.API.Configuration;

/// <summary>
/// Settings of the <c>Integrated</c> section, used when a game client runs the server for one singleplayer world and
/// controls it over standard input and output (see <c>docs/integrated-server.md</c>).
/// </summary>
public sealed class IntegratedConfiguration
{
    /// <summary>
    /// Whether the server runs as a client's integrated server.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// The world folder, laid out like a vanilla save. The server loads exactly this world instead of
    /// <c>config/worlds.json</c>, and creates it from the <c>NewWorld</c> settings when it has no <c>level.dat</c>.
    /// </summary>
    public string? WorldPath { get; set; }

    /// <summary>
    /// The name of the client's own player, who joins with <see cref="JoinSecret"/> instead of Mojang authentication.
    /// </summary>
    public string? LocalPlayerName { get; set; }

    /// <summary>
    /// The UUID the local player plays with.
    /// </summary>
    public Guid LocalPlayerUuid { get; set; }

    /// <summary>
    /// The per-launch secret the local player answers the login query with, passed in the environment
    /// (<c>Integrated__JoinSecret</c>) rather than on the command line.
    /// </summary>
    public string? JoinSecret { get; set; }
}
