using Microsoft.Extensions.Options;
using Obsidian.API.Configuration;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.Integrated;

/// <summary>
/// The state of an integrated server: who the client's local player is, and whether the world is open to LAN.
/// </summary>
public sealed class IntegratedSession(IOptions<IntegratedConfiguration> options)
{
    /// <summary>
    /// The login query channel the local player answers with the join secret.
    /// </summary>
    public const string JoinQueryChannel = "obsidian:integrated_join";

    private volatile Publication? publication;

    public IntegratedConfiguration Configuration => options.Value;

    /// <summary>
    /// Whether the world is open to LAN, which lets other players join.
    /// </summary>
    public bool IsPublished => this.publication is not null;

    /// <summary>
    /// Whether Open to LAN let every player use commands (vanilla's <c>setAllowCommandsForAllPlayers</c>).
    /// </summary>
    public bool AllowCommandsForAll => this.publication?.AllowCommands ?? false;

    /// <summary>
    /// Records that the world was opened to LAN. From then on other players may join.
    /// </summary>
    /// <param name="gameMode">The game mode forced on players who join from now on, or <c>null</c> for the world's.</param>
    /// <param name="allowCommands">Whether every player may use commands from now on.</param>
    public void Publish(GameMode? gameMode, bool allowCommands) => this.publication = new(gameMode, allowCommands);

    /// <summary>
    /// Whether <paramref name="username"/> is the local player's name, which only the local player may use. Names
    /// compare without case, like vanilla's.
    /// </summary>
    public bool IsLocalPlayer(string username) =>
        !string.IsNullOrEmpty(this.Configuration.LocalPlayerName)
        && string.Equals(username, this.Configuration.LocalPlayerName, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Whether <paramref name="player"/> is the local player, who joined with the join secret.
    /// </summary>
    public bool IsLocalPlayer(IPlayer player) => player.Uuid == this.Configuration.LocalPlayerUuid && this.IsLocalPlayer(player.Username);

    /// <summary>
    /// Checks the local player's answer to the join query against <see cref="IntegratedConfiguration.JoinSecret"/> in
    /// constant time. Without a configured secret, nobody can join as the local player.
    /// </summary>
    public bool VerifyJoinSecret(ReadOnlySpan<byte> answer)
    {
        if (string.IsNullOrEmpty(this.Configuration.JoinSecret))
            return false;

        return CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(this.Configuration.JoinSecret), answer);
    }

    /// <summary>
    /// The permission level a player gets on an integrated server, like vanilla's for its singleplayer owner: 4 for the
    /// local player when the world allows commands, and for everyone once Open to LAN allowed them; 0 otherwise.
    /// </summary>
    public int GetPermissionLevel(IPlayer player, bool worldAllowsCommands) =>
        this.AllowCommandsForAll || (worldAllowsCommands && this.IsLocalPlayer(player)) ? 4 : 0;

    /// <summary>
    /// The game mode forced on joining players, like vanilla's <c>IntegratedServer.getForcedGameType</c>: Open to LAN's
    /// (or the world's default when it chose none) once published, except in hardcore worlds.
    /// </summary>
    public GameMode? GetForcedGameMode(LevelData world) =>
        this.publication is Publication published && !world.Hardcore ? published.GameMode ?? world.DefaultGamemode : null;

    private sealed record Publication(GameMode? GameMode, bool AllowCommands);
}
