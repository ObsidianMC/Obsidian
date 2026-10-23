using Obsidian.API.Boss;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

public interface IServer : IDisposable
{
    /// <summary>
    /// Gets world-wide data in the default world. Use ILevel.GetSavedData for another world or level.
    /// </summary>
    public Data.SavedData<TValue> GetSavedData<TValue>(Data.SavedDataType<TValue> type, Func<TValue> create) =>
        this.DefaultWorld.GetSavedData(type, create, Data.SavedDataScope.World);

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
    /// <summary>
    /// No longer filled: which channels a connection may use is per connection, see <see cref="IClient.HasChannel"/>.
    /// </summary>
    [Obsolete("Channels are per connection; use IClient.HasChannel.")]
    public HashSet<string> RegisteredChannels { get; }

    public ReadOnlyMemory<byte> BrandData { get; }

    public ICommandHandler CommandHandler { get; }
    public IScoreboardManager ScoreboardManager { get; }
    public IEventDispatcher EventDispatcher { get; }
    public IWorldManager WorldManager { get; }

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

    public bool AddPlayer(IPlayer player);
    public bool RemovePlayer(IPlayer player);

    /// <summary>
    /// Creates, loads, and registers a server-side player without opening a network connection.
    /// </summary>
    /// <param name="uuid">The player's UUID.</param>
    /// <param name="username">The player's username.</param>
    /// <param name="world">The world to spawn the player in. Uses <see cref="DefaultWorld"/> when null.</param>
    public Task<IPlayer> AddServerPlayerAsync(Guid uuid, string username, IWorld? world = null);

    /// <summary>
    /// Removes a server-side player that was added through <see cref="AddServerPlayerAsync"/>: they leave like a
    /// network player, once their tick under way has ended.
    /// </summary>
    /// <returns>
    /// Whether the player was online and is leaving. Called from the player's own tick, the leave finishes after that
    /// tick, without being waited for; adding a player with their name waits for it meanwhile.
    /// </returns>
    public Task<bool> RemoveServerPlayerAsync(IPlayer player);

    public IBossBar CreateBossBar(ChatMessage title, float health, BossBarColor color, BossBarDivisionType divisionType, BossBarFlags flags);
}
