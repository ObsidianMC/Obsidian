using Obsidian.API.Boss;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

public interface IServer : IDisposable
{
    public string Version { get; }
    public int Port { get; }
    public int Tps { get; }
    public DateTimeOffset StartTime { get; }
    public ProtocolVersion Protocol { get; }
    public IOperatorList Operators { get; }
    public IWorld DefaultWorld { get; }
    public ServerConfiguration Configuration { get; }
    public ConcurrentDictionary<Guid, IPlayer> OnlinePlayers { get; }
    public ConcurrentDictionary<int, IClient> Connections { get; }
    public HashSet<string> RegisteredChannels { get; }

    public byte[] BrandData { get; }

    public ICommandHandler CommandHandler { get; }
    public IScoreboardManager ScoreboardManager { get; }
    public IEventDispatcher EventDispatcher { get; }

    public Task RunAsync();

    /// <summary>
    /// Checks if a player is online.
    /// </summary>
    /// <param name="username">The username you want to check for.</param>
    /// <returns>True if the player is online.</returns>
    public bool IsPlayerOnline(string username);

    /// <summary>
    /// Checks if a player is online.
    /// </summary>
    /// <param name="uuid">The uuid you want to check for.</param>
    /// <returns>True if the player is online.</returns>
    public bool IsPlayerOnline(Guid uuid);

    public bool IsWhitelisted(string username);
    public bool IsWhitelisted(Guid uuid);

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(ChatMessage message);

    /// <summary>
    /// Sends a message to all players on the specified world.
    /// </summary>
    public void BroadcastMessage(IWorld world, ChatMessage message);

    public IPlayer? GetPlayer(string username);
    public IPlayer? GetPlayer(Guid uuid);
    public IPlayer? GetPlayer(int entityId);

    /// <summary>
    /// Tries and gets an online player by username.
    /// </summary>
    /// <param name="username">The username of the player</param>
    /// <param name="player">The player if found.</param>
    /// <returns>True if a player is found, false if not.</returns>
    public bool TryGetPlayer(string username, [NotNullWhen(true)] out IPlayer? player);

    /// <summary>
    /// Tries and gets an online player by uuid.
    /// </summary>
    /// <param name="uuid">The uuid of the player</param>
    /// <param name="player">The player if found.</param>
    /// <returns>True if a player is found, false if not.</returns>
    public bool TryGetPlayer(Guid uuid, [NotNullWhen(true)] out IPlayer? player);

    /// <summary>
    /// Tries and gets an online player by username.
    /// </summary>
    /// <param name="entityId">The <see cref="IEntity.EntityId"/> of the player</param>
    /// <param name="player">The player if found.</param>
    /// <returns>True if a player is found, false if not.</returns>
    public bool TryGetPlayer(int entityId, [NotNullWhen(true)] out IPlayer? player);

    public void RegisterRecipes(params IRecipe[] recipes);

    public bool AddPlayer(IPlayer player);
    public bool RemovePlayer(IPlayer player);

    public IBossBar CreateBossBar(ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags);
}
