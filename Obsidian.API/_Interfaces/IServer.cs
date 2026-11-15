using Obsidian.API.Boss;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

/// <summary>
/// The Minecraft server: its players, worlds, configuration and services.
/// </summary>
/// <remarks>
/// During a plugin's calls, like its event handlers and commands, the server a plugin gets is its own view of this
/// one, whose scoreboards, boss bars and recipes are removed when the plugin unloads (see
/// <see cref="Events.BaseMinecraftEventArgs.Server"/>).
/// </remarks>
public interface IServer : IDisposable
{
    /// <summary>
    /// Gets world-wide data in the default world. Use ILevel.GetSavedData for another world or level.
    /// </summary>
    public Data.SavedData<TValue> GetSavedData<TValue>(Data.SavedDataType<TValue> type, Func<TValue> create) =>
        this.DefaultWorld.GetSavedData(type, create, Data.SavedDataScope.World);

    /// <summary>
    /// Gets the server's version.
    /// </summary>
    public string Version { get; }
    /// <summary>
    /// Gets the port the server listens on: the configured one until it starts, then the one it bound.
    /// </summary>
    public int Port { get; }
    /// <summary>
    /// Gets the measured number of ticks per second; 20 when the server keeps up.
    /// </summary>
    public int Tps { get; }
    /// <summary>
    /// Gets when the server started.
    /// </summary>
    public DateTimeOffset StartTime { get; }
    /// <summary>
    /// Gets the Minecraft protocol version the server speaks.
    /// </summary>
    public ProtocolVersion Protocol { get; }
    /// <summary>
    /// Gets the server's operators.
    /// </summary>
    public IOperatorList Operators { get; }
    /// <summary>
    /// Gets the world players join into.
    /// </summary>
    public IWorld DefaultWorld { get; }
    /// <summary>
    /// Gets the server's configuration.
    /// </summary>
    public ServerConfiguration Configuration { get; }
    /// <summary>
    /// Gets the online players by UUID. Use <see cref="AddPlayer"/> and <see cref="RemovePlayer"/> rather than changing
    /// it directly.
    /// </summary>
    public ConcurrentDictionary<Guid, IPlayer> OnlinePlayers { get; }
    /// <summary>
    /// Gets the open client connections by their player's entity id.
    /// </summary>
    public ConcurrentDictionary<int, IClient> Connections { get; }
    /// <summary>
    /// No longer filled: which channels a connection may use is per connection, see <see cref="IClient.HasChannel"/>.
    /// </summary>
    [Obsolete("Channels are per connection; use IClient.HasChannel.")]
    public HashSet<string> RegisteredChannels { get; }

    /// <summary>
    /// Gets the server's brand as the <c>minecraft:brand</c> payload sends it: a length-prefixed string.
    /// </summary>
    public ReadOnlyMemory<byte> BrandData { get; }

    /// <summary>
    /// Gets the server's command handler.
    /// </summary>
    public ICommandHandler CommandHandler { get; }
    /// <summary>
    /// Gets the server's scoreboard manager.
    /// </summary>
    public IScoreboardManager ScoreboardManager { get; }
    /// <summary>
    /// Gets the dispatcher that raises the server's events.
    /// </summary>
    public IEventDispatcher EventDispatcher { get; }
    /// <summary>
    /// Gets the server's world manager.
    /// </summary>
    public IWorldManager WorldManager { get; }

    /// <summary>
    /// Starts the server and runs it until it stops. The server calls it at startup; plugins shouldn't.
    /// </summary>
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

    /// <summary>
    /// Returns whether a player with <paramref name="username"/> is on the whitelist, ignoring case.
    /// </summary>
    /// <param name="username">The player's username.</param>
    public bool IsWhitelisted(string username);
    /// <summary>
    /// Returns whether the player with <paramref name="uuid"/> is on the whitelist.
    /// </summary>
    /// <param name="uuid">The player's UUID.</param>
    public bool IsWhitelisted(Guid uuid);

    /// <summary>
    /// Sends a message to all players on this server.
    /// </summary>
    public void BroadcastMessage(ChatMessage message);

    /// <summary>
    /// Sends a message to all players on the specified world.
    /// </summary>
    public void BroadcastMessage(IWorld world, ChatMessage message);

    /// <summary>
    /// Gets the online player with <paramref name="username"/>.
    /// </summary>
    /// <param name="username">The player's username.</param>
    /// <returns>The player, or <see langword="null"/> if none is online.</returns>
    public IPlayer? GetPlayer(string username);
    /// <summary>
    /// Gets the online player with <paramref name="uuid"/>.
    /// </summary>
    /// <param name="uuid">The player's UUID.</param>
    /// <returns>The player, or <see langword="null"/> if none is online.</returns>
    public IPlayer? GetPlayer(Guid uuid);
    /// <summary>
    /// Gets the online player with <paramref name="entityId"/> who has a client connection.
    /// </summary>
    /// <param name="entityId">The player's entity id.</param>
    /// <returns>The player, or <see langword="null"/> if none is found.</returns>
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

    /// <summary>
    /// Registers recipes, which crafting grids, furnaces and players' recipe books use like vanilla's from now on;
    /// online players' books follow. Those a plugin registers through its own server are removed when it unloads.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// A recipe's identifier, in snake case, is taken; the recipes before it were registered.
    /// </exception>
    public void RegisterRecipes(params IRecipe[] recipes);

    /// <summary>The current recipe snapshot's process-local revision, changing whenever it is replaced.</summary>
    public long CraftingRecipeRevision => 0;

    /// <summary>
    /// Evaluates a square crafting grid of width 2 or 3, without changing its stacks. Uses the same recipes and
    /// matching as the player's grid and crafting table. Returns null when nothing matches.
    /// </summary>
    public CraftingEvaluation? EvaluateCrafting(IReadOnlyList<ItemStack?> grid, int width) => null;

    /// <summary>
    /// Gets a detached built-in recipe by its identifier, or null. Custom plugin recipe types use GetRecipes.
    /// </summary>
    public IRecipe? GetRecipe(string identifier) => null;

    /// <summary>
    /// Finds the first matching recipe of a plugin's type in the current set, by lexical recipe identifier.
    /// Returns null when none matches or the type is unloaded. Do not retain a result across reloads.
    /// </summary>
    public TRecipe? FindRecipe<TRecipe>(RecipeType<TRecipe> type, IReadOnlyList<ItemStack?> inputs)
        where TRecipe : class => null;

    /// <summary>
    /// The current recipes of a plugin's type, in lexical recipe identifier order, like a machine's screen lists
    /// them. Empty when the type is unloaded. Do not retain the recipes across reloads.
    /// </summary>
    public IReadOnlyList<TRecipe> GetRecipes<TRecipe>(RecipeType<TRecipe> type)
        where TRecipe : class => [];

    /// <summary>
    /// Registers <paramref name="player"/> as online, by UUID and username.
    /// </summary>
    /// <param name="player">The player to register.</param>
    /// <returns><see langword="true"/> if the player was added; <see langword="false"/> if one with the UUID is online.</returns>
    public bool AddPlayer(IPlayer player);
    /// <summary>
    /// Unregisters <paramref name="player"/> and removes them from their level.
    /// </summary>
    /// <param name="player">The player to remove; only the registered instance is removed.</param>
    /// <returns><see langword="true"/> if the player was removed; otherwise, <see langword="false"/>.</returns>
    public bool RemovePlayer(IPlayer player);

    /// <summary>
    /// Creates, loads, and registers a server-side player without opening a network connection.
    /// </summary>
    /// <param name="uuid">The player's UUID.</param>
    /// <param name="username">The player's username.</param>
    /// <param name="world">The world to spawn the player in. Uses <see cref="DefaultWorld"/> when null.</param>
    /// <exception cref="InvalidOperationException">
    /// A player with the name is still leaving and it's called from inside a level's tick, or a player's tick, grant or
    /// save, which the leave may be waiting for. Add them once the ticks are over.
    /// </exception>
    public Task<IPlayer> AddServerPlayerAsync(Guid uuid, string username, IWorld? world = null);

    /// <summary>
    /// Removes a server-side player that was added through <see cref="AddServerPlayerAsync"/>: they leave like a
    /// network player, once their tick under way has ended.
    /// </summary>
    /// <returns>
    /// Whether the player was online and is leaving. Called from inside a level's tick, or any player's tick, grant or
    /// save (the player's own included), the leave finishes after the ticks, without being waited for; adding a player
    /// with their name waits for it meanwhile.
    /// </returns>
    public Task<bool> RemoveServerPlayerAsync(IPlayer player);

    /// <summary>
    /// Creates a boss bar, shown to no one until players are added to it.
    /// </summary>
    /// <param name="title">The text shown above the bar.</param>
    /// <param name="health">How full the bar is, from 0 to 1.</param>
    /// <param name="color">The bar's color.</param>
    /// <param name="divisionType">How many notches the bar is divided into.</param>
    /// <param name="flags">The bar's flags, like darkening the sky.</param>
    /// <returns>The new boss bar.</returns>
    public IBossBar CreateBossBar(ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags);
}
