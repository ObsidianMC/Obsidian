using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Obsidian.API.Boss;
using Obsidian.API.Commands;
using Obsidian.API.Configuration;
using Obsidian.API.Crafting;
using Obsidian.API.Inventory;
using Obsidian.API.Events;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Modules;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Integrated;
using Obsidian.Net;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Plugins.ServiceProviders;
using Obsidian.Services;
using Obsidian.WorldData;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace Obsidian;

public sealed partial class Server : IServer, IPluginScopedServer
{
    private static int EntityCounter;

    internal static readonly ConcurrentDictionary<string, DateTimeOffset> throttler = new();

    // Leaves under way, with the name of the player leaving.
    private readonly ConcurrentDictionary<Task, string> pendingLeaves = new();

    internal readonly CancellationTokenSource cancelTokenSource;
    internal readonly ILogger logger;

    public ReadOnlyMemory<byte> BrandData
    {
        get
        {
            var buffer = new NetworkBuffer();
            buffer.WriteString(this.Brand);

            return buffer.GetBuffer();
        }
    }

    private readonly IUserCache userCache;
    private readonly ILoggerFactory loggerFactory;
    private readonly IServiceProvider serviceProvider;
    private readonly IDisposable? configWatcher;
    private readonly object shutdownLock = new();
    private Task? shutdownTask;
    private Task loopTask = Task.CompletedTask;

    // The keep-alive and chunk loops, which run beside the tick loop and stop with it.
    private Task connectionLoopsTask = Task.CompletedTask;

    public IOptionsMonitor<WhitelistConfiguration> WhitelistConfiguration { get; }

    public ProtocolVersion Protocol => ServerConstants.DefaultProtocol;
    public int Tps { get; private set; }

    internal string TickStage { get; private set; } = "not started";
    public DateTimeOffset StartTime { get; private set; }

    public PluginManager PluginManager { get; }
    public IEventDispatcher EventDispatcher { get; }

    public IOperatorList Operators { get; }
    public IScoreboardManager ScoreboardManager { get; private set; }
    public IWorldManager WorldManager { get; }

    public ConcurrentDictionary<Guid, IPlayer> OnlinePlayers { get; } = [];
    private ConcurrentDictionary<string, Guid> UsernameToUuidMappings { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> RegisteredChannels { get; } = [];

    /// <summary>
    /// The payload channels plugins registered; the plugin manager removes a plugin's when it unloads.
    /// </summary>
    internal Net.Payloads.PayloadChannelRegistry PayloadChannels { get; } = new();

    /// <summary>
    /// The registries plugins added to, which clients must agree with the server on: vanilla clients can't join while
    /// there are any, and NeoForge and Fabric clients are sent them. Clients number block states from the block
    /// registry's order, so syncing it gives plugins' block states the same ids as on the server.
    /// </summary>
    internal IReadOnlyList<Net.Payloads.SyncedRegistry> GetModdedRegistries() =>
        Net.Payloads.SyncedRegistries.GetModded();

    /// <summary>
    /// What clients configure with that plugins change: the registries plugins added to, whose ids only grow, so their
    /// sizes tell them apart, with how many times plugins' data files changed biomes, dimension types or damage types
    /// (whose data clients are sent), and the plugins' payload channels.
    /// </summary>
    internal ContentVersion GetContentVersion()
    {
        var registries = $"{ItemsRegistry.All.Count}/{BlocksRegistry.PluginBlocks.Count}/"
            + $"{EntityDefinitionsRegistry.PluginDefinitions.Count}/{CodecRegistry.Biomes.All.Count}/"
            + $"{CodecRegistry.Dimensions.All.Count}/{CodecRegistry.DamageType.All.Count}/{PluginCodecs.Revision}/"
            + $"{MenuTypesRegistry.PluginMenus.Count}/{ParticleTypesRegistry.PluginParticles.Count}/"
            + $"{BlockEntityTypesRegistry.PluginTypes.Count}/"
            + $"{DataComponentTypesRegistry.PluginTypes.Count(type => type.IsSynced)}/"
            + $"{CustomStatisticsRegistry.Plugins.Length}/"
            + DialogRegistry.Fingerprint;

        Net.Payloads.NetworkProtocol[] protocols =
            [Net.Payloads.NetworkProtocol.Configuration, Net.Payloads.NetworkProtocol.Play];

        var channels = string.Join(';', protocols
            .SelectMany(protocol => this.PayloadChannels
                .GetComponents(protocol)
                .Select(component => $"{protocol}:{component.Id}={component.Version}:{component.Flow}:"
                    + $"{component.Optional}"))
            .Order(StringComparer.Ordinal));

        return new ContentVersion(registries, channels, PluginTags.Revision);
    }

    /// <summary>
    /// Has each connected client catch up once plugins changed the content: one configured with other registries, or,
    /// for a client that agreed on channels, other channels, goes back through configuration; the others get the
    /// current tags, like vanilla's <c>/reload</c> sends them.
    /// </summary>
    internal async Task RefreshClientsAsync()
    {
        // One at a time, so an earlier refresh can't send its older tags after a later one.
        await this.refreshing.WaitAsync();
        try
        {
            await this.RefreshClientsOnceAsync();
        }
        finally
        {
            this.refreshing.Release();
        }
    }

    private readonly SemaphoreSlim refreshing = new(1, 1);

    private async Task RefreshClientsOnceAsync()
    {
        var current = this.GetContentVersion();
        var tags = this.CreateDialogConfiguration().Tags;

        foreach (var client in this.Connections.Values.OfType<Client>())
        {
            if (client.State != ClientState.Play
                || client.AwaitingReconfiguration
                || client.ConfiguredContent is null)
            {
                continue;
            }

            if (client.NeedsReconfiguration(current))
                client.RequestReconfiguration();
            else
                await client.QueuePacketAsync(UpdateTagsPacket.ClientboundPlay with { Tags = tags });
        }
    }

    public ICommandHandler CommandHandler { get; }
    public ServerConfiguration Configuration { get; set; }
    public string Version => ServerConstants.VERSION;

    public string Brand { get; } = "obsidian";

    /// <summary>
    /// The port the server listens on: the configured one until it's started, then the bound one.
    /// </summary>
    public int Port { get; private set; }

    /// <summary>
    /// Whether the worlds are paused, like an integrated server while its game is paused: they don't tick, and the ticks
    /// don't count toward the autosave, while connections keep running. See <see cref="PauseAsync"/> and
    /// <see cref="Resume"/>.
    /// </summary>
    public bool Paused { get; private set; }

    // Held while the worlds tick, so pausing waits for the tick in progress.
    private readonly SemaphoreSlim tickGate = new(1, 1);

    /// <summary>
    /// Raised when a save of everything starts, with whether it's an autosave.
    /// </summary>
    public event Action<bool>? SaveStarted;

    /// <summary>
    /// Raised when a save of everything ends, with whether it was an autosave and its failure, if it failed.
    /// </summary>
    public event Action<bool, Exception?>? SaveCompleted;

    /// <summary>
    /// Raised when handling a player's leave failed, so their data may not have been saved, with a message saying so.
    /// Unlike a failed save of everything, a later save doesn't make up for it: the player is gone.
    /// </summary>
    public event Action<string>? PlayerSaveFailed;

    /// <summary>
    /// The integrated server's session, when a game client runs this server.
    /// </summary>
    internal IntegratedSession? Integrated { get; }

    // Vanilla's autosave interval (MinecraftServer's autosave period): every 6000 ticks, 5 minutes at 20 TPS.
    private const int AutosaveInterval = 6000;

    // Saves of everything run one at a time.
    private readonly SemaphoreSlim saveLock = new(1, 1);
    public IWorld DefaultWorld => WorldManager.DefaultWorld;

    /// <summary>
    /// Creates a new instance of <see cref="Server"/>.
    /// </summary>
    public Server(
        IHostApplicationLifetime lifetime,
        IOptionsMonitor<ServerConfiguration> configuration,
        IOptionsMonitor<WhitelistConfiguration> whitelistConfiguration,
        ILoggerFactory loggerFactory,
        EventDispatcher eventDispatcher,
        IServiceProvider serviceProvider,
        CommandHandler commandHandler,
        IUserCache userCache,
        IWorldManager worldManager)
    {
        this.logger = loggerFactory.CreateLogger<Server>();
        this.cancelTokenSource = CancellationTokenSource.CreateLinkedTokenSource(lifetime.ApplicationStopping);

        this.serviceProvider = serviceProvider;
        this.WhitelistConfiguration = whitelistConfiguration;
        this.loggerFactory = loggerFactory;
        this.configWatcher = configuration.OnChange((config) =>
        {
            this.Configuration = config;
        });
        this.userCache = userCache;
        this.EventDispatcher = eventDispatcher;
        this.WorldManager = worldManager;

        var config = configuration.CurrentValue;

        this.Configuration = config;
        this.Port = config.Port;
        this.Integrated = serviceProvider.GetService<IntegratedSession>();

        this.Operators = new OperatorList(this, loggerFactory, this.Integrated);
        this.CommandHandler = commandHandler;
        this.PluginManager = ActivatorUtilities.CreateInstance<PluginManager>(this.serviceProvider, this);
    }

    public static int GetNextEntityId() => Interlocked.Increment(ref EntityCounter);
    internal static int ReserveEntityIds(int count) => Interlocked.Add(ref EntityCounter, count) - count + 1;

    private ServerRecipeRegistry? recipeRegistry;

    internal ServerRecipeRegistry RecipeRegistry => LazyInitializer.EnsureInitialized(
        ref this.recipeRegistry, () => new ServerRecipeRegistry(this.GetContentVersion));

    /// <remarks>
    /// Recipes registered through a plugin's <see cref="IServer"/> are the plugin's, which unloading removes.
    /// </remarks>
    public void RegisterRecipes(params IRecipe[] recipes) => this.RegisterRecipes(recipes, owner: null);

    public long CraftingRecipeRevision => this.RecipeRegistry.Current.Revision;

    public CraftingEvaluation? EvaluateCrafting(IReadOnlyList<ItemStack?> grid, int width) =>
        this.RecipeRegistry.Current.EvaluateCrafting(grid, width);

    public IRecipe? GetRecipe(string identifier)
    {
        ArgumentNullException.ThrowIfNull(identifier);
        return this.RecipeRegistry.Current.Recipes.TryGetValue(identifier, out var recipe)
            ? RecipesRegistry.Snapshot(recipe)
            : null;
    }

    public TRecipe? FindRecipe<TRecipe>(RecipeType<TRecipe> type, IReadOnlyList<ItemStack?> inputs)
        where TRecipe : class
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(inputs);
        return this.RecipeRegistry.Current.FindPluginRecipe(type, inputs);
    }

    public IReadOnlyList<TRecipe> GetRecipes<TRecipe>(RecipeType<TRecipe> type)
        where TRecipe : class
    {
        ArgumentNullException.ThrowIfNull(type);
        return this.RecipeRegistry.Current.GetPluginRecipes(type);
    }

    internal void LoadRecipeFiles(
        IEnumerable<(PluginContainer Owner, IEnumerable<RecipeFile> Files)> sources)
    {
        this.RecipeRegistry.ReplaceFiles(sources, this.logger);
        this.OnRecipesChanged();
    }

    /// <summary>
    /// Registers recipes for <paramref name="owner"/>, whose unloading removes them, or for the server. They're used
    /// like vanilla's from now on, and online players' recipe books follow (see <see cref="SyncRecipesAsync"/>).
    /// </summary>
    /// <exception cref="InvalidOperationException">The owner is unloading; the recipes were removed.</exception>
    internal void RegisterRecipes(IRecipe[] recipes, PluginContainer? owner)
    {
        var added = new List<IRecipe>();

        try
        {
            this.RecipeRegistry.Register(recipes, added);
        }
        finally
        {
            if (added.Count > 0)
                this.OnRecipesChanged();

            owner?.TrackAddition(() => this.RemoveRecipes(added));
        }
    }

    // Removes recipes a plugin added, unless another recipe took their identifier since.
    private void RemoveRecipes(List<IRecipe> added)
    {
        this.RecipeRegistry.Unregister(added);
        this.OnRecipesChanged();
    }

    // Players catch up with the recipes without the change (a plugin unloading, say) waiting for them.
    private void OnRecipesChanged() => _ = this.SyncRecipesAsync();

    /// <summary>
    /// Has online players catch up with the current recipes: their recipe books get the recipes added and removed
    /// since they were last sent theirs, and an open crafting grid's result is worked out again. Players whose client
    /// goes back through configuration for other content are left out; they get the whole book when they join again,
    /// as the new recipes may show items their client doesn't know yet.
    /// </summary>
    /// <remarks>
    /// Runs one at a time, so that awaiting it after a change also waits for the sync the change started.
    /// </remarks>
    internal async Task SyncRecipesAsync()
    {
        await this.recipeSync.WaitAsync();
        try
        {
            foreach (var player in this.OnlinePlayers.Values)
            {
                try
                {
                    await player.SynchronizeRecipeBookAsync();
                }
                catch (Exception ex)
                {
                    // A failed book keeps its last-sent identities; the next change also resends the window.
                    Log.RecipeSyncFailed(this.logger, ex, player.Username);
                }
            }
        }
        finally
        {
            this.recipeSync.Release();
        }
    }

    private readonly SemaphoreSlim recipeSync = new(1, 1);

    public bool IsPlayerOnline(string username) => this.UsernameToUuidMappings.ContainsKey(username);

    public bool IsPlayerOnline(Guid uuid) => OnlinePlayers.ContainsKey(uuid);

    public IPlayer? GetPlayer(string username)
    {
        if (this.UsernameToUuidMappings.TryGetValue(username, out var uuid) && OnlinePlayers.TryGetValue(uuid, out var player))
            return player;

        return null;
    }

    public IPlayer? GetPlayer(Guid uuid) => OnlinePlayers.TryGetValue(uuid, out var player) ? player : null;

    public IPlayer? GetPlayer(int entityId)
    {
        if (this.Connections.TryGetValue(entityId, out var client) && client.Player is { } connectedPlayer &&
            OnlinePlayers.TryGetValue(connectedPlayer.Uuid, out var player))
            return player;

        return this.OnlinePlayers.Values.FirstOrDefault(player => player.EntityId == entityId);
    }

    public bool TryGetPlayer(string username, [NotNullWhen(true)] out IPlayer? player)
    {
        if (this.GetPlayer(username) is IPlayer foundPlayer)
        {
            player = foundPlayer;
            return true;
        }

        player = null;
        return false;
    }

    
    public bool TryGetPlayer(Guid uuid, [NotNullWhen(true)] out IPlayer? player) => this.OnlinePlayers.TryGetValue(uuid, out player);

    public bool TryGetPlayer(int entityId, [NotNullWhen(true)] out IPlayer? player)
    {
        if (this.GetPlayer(entityId) is IPlayer foundPlayer)
        {
            player = foundPlayer;
            return true;
        }

        player = null;
        return false;
    }

    public void BroadcastMessage(ChatMessage message)
    {
        this.DefaultWorld.PacketBroadcaster.QueuePacket(new SystemChatPacket(message, false));
        Log.Broadcast(this.logger, message.Text);
    }

    public void BroadcastMessage(IWorld world, ChatMessage message)
    {
        this.DefaultWorld.PacketBroadcaster.QueuePacketToLevel(world, new SystemChatPacket(message, false));
        Log.Broadcast(this.logger, message.Text);
    }

    /// <summary>
    /// Whether <paramref name="player"/> may change or lock the world's difficulty: like vanilla, a game master
    /// (permission level 2) or the owner of a singleplayer world, whether or not it allows commands.
    /// </summary>
    internal bool MayChangeDifficulty(IPlayer player) =>
        this.Integrated?.IsLocalPlayer(player) == true || ((OperatorList)this.Operators).GetPermissionLevel(player) >= 2;

    /// <summary>Tells every player the world's difficulty and whether it's locked.</summary>
    internal void BroadcastDifficulty() =>
        this.DefaultWorld.PacketBroadcaster.QueuePacket(Net.Packets.Play.Clientbound.ChangeDifficultyPacket.Of(this.DefaultWorld.LevelData));

    /// <summary>
    /// Starts this server asynchronously.
    /// </summary>
    public async Task RunAsync()
    {
        Log.Starting(this.logger, this.Version);

        this.CommandHandler.RegisterCommands();
        this.EventDispatcher.RegisterEvents();

        if (this.Integrated is not null)
            ((CommandHandler)this.CommandHandler).RegisterCommandClass(null, typeof(IntegratedCommandModule));

        Directory.CreateDirectory(ServerConstants.PermissionPath);
        Directory.CreateDirectory(ServerConstants.PersistentDataPath);
        Directory.CreateDirectory(ServerConstants.PluginsPath);
        Directory.CreateDirectory(ServerConstants.DataPacksPath);

        StartTime = DateTimeOffset.Now;
        this.Connections = new ConcurrentDictionary<int, IClient>(-1, this.MaxConnections);

        var loadTimeStopwatch = Stopwatch.StartNew();

        // Check if MPDM and OM are enabled, if so, we can't handle connections
        if (Configuration.Network.MulitplayerDebugMode && Configuration.OnlineMode)
        {
            Log.IncompatibleDebugMode(this.logger);
            await StopAsync();
            return;
        }

        await RecipesRegistry.InitializeAsync();

        await this.userCache.LoadAsync(this.cancelTokenSource.Token);

        await (Operators as OperatorList).InitializeAsync();

        await PluginManager.LoadPluginsAsync();

        if (!Configuration.OnlineMode)
            Log.OfflineMode(this.logger);

        CommandsRegistry.Register(this);

        this.loopTask = LoopAsync();
        this.connectionLoopsTask = Task.WhenAll(KeepAliveLoopAsync(), ChunkLoopAsync());

        // A failure here reaches the host, which reports the crash. A stop while the worlds load shuts down gracefully too.
        try
        {
            if (await this.StartWhenWorldsLoadAsync())
            {
                loadTimeStopwatch.Stop();
                Log.Ready(this.logger, loadTimeStopwatch.Elapsed, this.Port);
            }

            await this.loopTask;
        }
        finally
        {
            // Try to shut the server down gracefully.
            await this.StopAsync();
            Log.Stopped(this.logger);
        }
    }

    /// <summary>
    /// Waits for the worlds to load, then starts accepting connections, unless the server stops first.
    /// </summary>
    /// <returns>Whether the server started.</returns>
    private async Task<bool> StartWhenWorldsLoadAsync()
    {
        // Polling with a delay leaves the cores to world generation instead of spinning one.
        while (!this.WorldManager.ReadyToJoin)
        {
            if (this.Stopping)
                return false;

            await Task.Delay(50);
        }

        ScoreboardManager = new ScoreboardManager(this, this.loggerFactory);

        await this.PluginManager.OnServerReadyAsync();

        try
        {
            await this.StartAsync(this.Port);
        }
        catch (InvalidOperationException) when (this.Stopping)
        {
            // Stopped just before the listener opened (see ListenAsync).
            return false;
        }

        return true;
    }

    /// <remarks>
    /// Boss bars created through a plugin's <see cref="IServer"/> are the plugin's: unloading it hides them and
    /// refuses their later changes.
    /// </remarks>
    public IBossBar CreateBossBar(
        ChatMessage title,
        float health,
        BossBarColor color,
        BossBarDivisionType divisionType,
        BossBarFlags flags) =>
        this.CreateBossBar(title, health, color, divisionType, flags, owner: null);

    /// <summary>
    /// Creates a boss bar for <paramref name="owner"/>, whose unloading retires it, or for the server.
    /// </summary>
    /// <exception cref="InvalidOperationException">The owner is unloading.</exception>
    internal BossBar CreateBossBar(
        ChatMessage title,
        float health,
        BossBarColor color,
        BossBarDivisionType divisionType,
        BossBarFlags flags,
        PluginContainer? owner)
    {
        var bar = ActivatorUtilities.CreateInstance<BossBar>(
            this.serviceProvider,
            title,
            health,
            color,
            divisionType,
            flags);

        if (owner is not null && !owner.BossBars.TryAdd(bar))
            throw new InvalidOperationException($"{owner.Info.Name} is unloading.");

        return bar;
    }

    public async Task ExecuteCommand(string input)
    {
        var context = new CommandContext(CommandHelpers.DefaultPrefix + input, new CommandSender(CommandIssuers.Console, null), null, this);

        await CommandHandler.ProcessCommand(context);
    }

    /// <summary>
    /// Whether the server is stopping or stopped: nothing new (saves, listeners) should start.
    /// </summary>
    internal bool Stopping => this.cancelTokenSource.IsCancellationRequested;

    /// <summary>
    /// Stops the server once: every call returns the same shutdown.
    /// </summary>
    public Task StopAsync()
    {
        lock (this.shutdownLock)
            return this.shutdownTask ??= this.StopCoreAsync();
    }

    private async Task StopCoreAsync()
    {
        await cancelTokenSource.CancelAsync();

        this.CloseListeners();

        try
        {
            // The loop disconnects the players once it stops, so their leaves save them before the worlds are.
            await Task.WhenAll(this.loopTask, this.connectionLoopsTask);
        }
        finally
        {
            await this.PendingLeavesAsync();

            // The final save waits for a save in progress (a pause, the save command, an autosave), so they never
            // overlap.
            await this.saveLock.WaitAsync();
            try
            {
                // Worlds that didn't finish loading aren't saved: a new world whose generation was stopped keeps no
                // level.dat, so it isn't later taken for a complete world.
                if (this.WorldManager.ReadyToJoin)
                    await WorldManager.FlushLoadedWorldsAsync();

                await WorldManager.DisposeAsync();
                await this.PluginManager.DisposeAsync();

                await this.userCache.SaveAsync();
            }
            finally
            {
                this.saveLock.Release();
            }
        }
    }
    /// <summary>
    /// Pauses the worlds between two ticks: once this returns, no world tick runs, and no chunk loads, unloads or
    /// starts generating, until <see cref="Resume"/>. Saves still run, and connections keep running.
    /// </summary>
    /// <returns>Whether the worlds were running, so this call paused them.</returns>
    public async Task<bool> PauseAsync()
    {
        await this.tickGate.WaitAsync();
        try
        {
            var wasRunning = !this.Paused;
            this.Paused = true;

            // Like vanilla's paused integrated server, whose chunk loading and unloading are part of its tick.
            if (this.WorldManager is WorldData.WorldManager worldManager)
                await worldManager.PauseChunkManagementAsync();

            return wasRunning;
        }
        finally
        {
            this.tickGate.Release();
        }
    }

    /// <summary>
    /// Lets the worlds tick, and their chunks load and unload, again from the next tick.
    /// </summary>
    public void Resume()
    {
        (this.WorldManager as WorldData.WorldManager)?.ResumeChunkManagement();
        this.Paused = false;
    }

    /// <summary>
    /// Saves everything: the online players, the worlds with their regions and level data, and the user cache. Saves run
    /// one at a time; a failed one is logged and reported through <see cref="SaveCompleted"/>.
    /// </summary>
    /// <param name="autosave">Whether this is the periodic autosave, rather than one that was asked for.</param>
    /// <returns>Whether the save succeeded.</returns>
    public async Task<bool> SaveEverythingAsync(bool autosave)
    {
        await this.saveLock.WaitAsync();
        try
        {
            Log.SavingWorlds(this.logger);
            this.SaveStarted?.Invoke(autosave);

            try
            {
                foreach (var player in this.OnlinePlayers.Values)
                    await player.SaveAsync();

                await WorldManager.FlushLoadedWorldsAsync();
                await this.userCache.SaveAsync();
            }
            catch (Exception ex)
            {
                Log.AutosaveFailed(this.logger, ex);
                this.SaveCompleted?.Invoke(autosave, ex);
                return false;
            }

            this.SaveCompleted?.Invoke(autosave, null);
            return true;
        }
        finally
        {
            this.saveLock.Release();
        }
    }

    public bool AddPlayer(IPlayer player)
    {
        if (!this.OnlinePlayers.TryAdd(player.Uuid, player))
            return false;

        // The name must map to this player alone; undo the registration rather than leave lookups disagreeing.
        if (this.UsernameToUuidMappings.TryAdd(player.Username, player.Uuid))
            return true;

        this.OnlinePlayers.TryRemove(new KeyValuePair<Guid, IPlayer>(player.Uuid, player));
        return false;
    }

    public Task<IPlayer> AddServerPlayerAsync(Guid uuid, string username, IWorld? world = null) =>
        this.AddServerPlayerAsync(uuid, username, world, PluginEntities.FindSpawner());

    /// <summary>
    /// Adds a server-side player for <paramref name="plugin"/>, which unloading the plugin removes, or for the server.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// The player is online already, joining failed, or <paramref name="plugin"/> is unloading.
    /// </exception>
    internal async Task<IPlayer> AddServerPlayerAsync(
        Guid uuid,
        string username,
        IWorld? world,
        PluginContainer? plugin)
    {
        plugin?.ThrowIfClosing();

        var player = new ServerPlayer(uuid, username, this, world ?? this.DefaultWorld)
        {
            SpawnedBy = plugin
        };
        await player.LoadAsync();

        if (!this.AddPlayer(player))
            throw new InvalidOperationException($"Player '{username}' ({uuid}) is already online.");

        try
        {
            // The internal join handler registers the player in its level and fails the event if it can't.
            var result = await this.EventDispatcher.ExecuteEventAsync(new PlayerJoinEventArgs(player, this, DateTimeOffset.Now));
            if (result != EventResult.Completed)
                throw new InvalidOperationException($"Joining server-side player '{username}' failed with result {result}.");

            plugin?.ThrowIfClosing();
            if (!player.CompleteJoin())
                throw new InvalidOperationException($"Joining server-side player '{username}' was cancelled.");
        }
        catch
        {
            try
            {
                await MainEventHandler.DespawnPlayerAsync(player);
            }
            finally
            {
                player.CancelJoin();
                player.Level.TryRemovePlayer(player);
                this.RemovePlayer(player);
            }
            throw;
        }

        return player;
    }

    /// <summary>
    /// The server as the code running now sees it: a plugin's own instance during the plugin's calls (see
    /// <see cref="PluginCall.CurrentPlugin"/>), so that event arguments, command contexts and connections hand each
    /// plugin its own.
    /// </summary>
    IServer IPluginScopedServer.ForCurrentCall() =>
        PluginCall.CurrentPlugin?.HostServices?.Server is PluginServer own && own.Wraps(this) ? own : this;

    /// <summary>
    /// Keeps track of a player leaving after their connection closed, whose save stopping the server, and the player
    /// joining again (<see cref="WaitForLeavesAsync"/>), waits for.
    /// </summary>
    internal void TrackLeave(Task leave, string username)
    {
        if (leave.IsCompleted)
            return;

        this.pendingLeaves.TryAdd(leave, username);
        _ = leave.ContinueWith(finished => this.pendingLeaves.TryRemove(finished, out _), TaskScheduler.Default);
    }

    /// <summary>Reports through <see cref="PlayerSaveFailed"/> that a player's data may not have been saved as they left.</summary>
    internal void ReportPlayerSaveFailed(string username) =>
        this.PlayerSaveFailed?.Invoke($"Saving {username} as they left failed.");

    // Leaves log their own failures, so waiting for them never throws.
    private Task PendingLeavesAsync() => Task.WhenAll(this.pendingLeaves.Keys);

    /// <summary>
    /// Waits until a player's earlier connection has left and saved them, so that joining again loads what it saved.
    /// </summary>
    internal Task WaitForLeavesAsync(string username) => Task.WhenAll(this.pendingLeaves
        .Where(entry => string.Equals(entry.Value, username, StringComparison.OrdinalIgnoreCase))
        .Select(entry => entry.Key));

    public bool RemovePlayer(IPlayer player)
    {
        // Only the registered instance can unregister, so a stale reference can't remove a newer player who reused
        // the UUID or name.
        if (!this.OnlinePlayers.TryRemove(new KeyValuePair<Guid, IPlayer>(player.Uuid, player)))
            return false;

        this.ReleaseRegistrations(player);
        return true;
    }

    // Drops the lookups that hang off an online registration; the caller must have just removed that registration.
    private void ReleaseRegistrations(IPlayer player)
    {
        this.UsernameToUuidMappings.TryRemove(new KeyValuePair<string, Guid>(player.Username, player.Uuid));
        player.Level.TryRemovePlayer(player);
    }

    public async Task<bool> RemoveServerPlayerAsync(IPlayer player)
    {
        // Claiming the registration first means concurrent removals run the leave lifecycle only once.
        if (player is not ServerPlayer serverPlayer
            || !this.OnlinePlayers.TryRemove(new KeyValuePair<Guid, IPlayer>(player.Uuid, player)))
            return false;

        serverPlayer.CancelJoin();

        // Like a network player's leave, it waits for the player's tick under way. One asked for from that tick (a
        // plugin removing the player as they die, say) runs apart from it, once it's done, and isn't waited for.
        if (!serverPlayer.IsInOwnTick)
        {
            await this.LeaveServerPlayerAsync(serverPlayer);
            return true;
        }

        Task leave;
        using (ExecutionContext.SuppressFlow())
        {
            leave = Task.Run(async () =>
            {
                // Nobody awaits it, so it logs its own failure, like a network player's leave.
                try
                {
                    await this.LeaveServerPlayerAsync(serverPlayer);
                }
                catch (Exception ex)
                {
                    Log.ServerPlayerLeaveFailed(this.logger, ex, serverPlayer.Username);
                }
            });
        }

        this.TrackLeave(leave, serverPlayer.Username);
        return true;
    }

    // The leave of a server-side player whose registration was claimed.
    private async Task LeaveServerPlayerAsync(ServerPlayer player)
    {
        var result = EventResult.Failed;
        try
        {
            // Its ticks stop first, so that nothing it does runs after it left.
            await player.StopTickingAsync();

            result = await this.EventDispatcher.ExecuteEventAsync(new PlayerLeaveEventArgs(player, this, DateTimeOffset.Now));
        }
        finally
        {
            try
            {
                // A failing handler stops the ones after it, which may include the internal one that despawns the player.
                if (result != EventResult.Completed)
                    await MainEventHandler.DespawnPlayerAsync(player);
            }
            finally
            {
                // No longer a viewer of what they had open, so other viewers' clicks don't update them.
                player.OpenedContainer = null;
                player.RunMenusClosed();
                this.ReleaseRegistrations(player);
            }
        }
    }

    // When the world tick in progress started (a Stopwatch timestamp), or 0 between ticks; see KeepAliveLoopAsync.
    private long tickStarted;

    // Failures of the world tick and of each connection loop, each logged now and then rather than on every tick.
    private readonly ThrottledFailures tickFailures = new();
    private readonly ThrottledFailures keepAliveFailures = new();
    private readonly ThrottledFailures chunkFailures = new();

    private static readonly TimeSpan StuckTickWarningAfter = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan StuckTickWarningInterval = TimeSpan.FromMinutes(1);

    private void ReportTickFailure(Exception exception)
    {
        if (this.tickFailures.ShouldLog(out var suppressed))
            Log.TickFailed(this.logger, exception, suppressed);
    }

    /// <summary>
    /// Keeps connections alive on its own timer, so a slow or stuck world tick doesn't also time every player out. It
    /// also warns while a world tick is stuck, with the stage each level is at. Chunks go out from
    /// <see cref="ChunkLoopAsync"/> instead, since a chunk read from disk can take long enough to delay keep-alives.
    /// </summary>
    private async Task KeepAliveLoopAsync()
    {
        var timer = new BalancingTimer(50, cancelTokenSource.Token);
        var keepAliveTicks = 0;
        var nextStuckWarning = StuckTickWarningAfter;
        var observedTick = 0L;

        try
        {
            while (await timer.WaitForNextTickAsync())
            {
                if (++keepAliveTicks > Configuration.Network.KeepAliveInterval / 50)
                {
                    keepAliveTicks = 0;
                    await this.ForEachConnectionAsync(SendKeepAliveAsync, this.keepAliveFailures);
                }

                // Each tick gets its own warnings. Comparing start times also catches a tick that began right after
                // the previous one ended, between two checks, so it doesn't inherit that tick's later threshold.
                var started = Volatile.Read(ref this.tickStarted);
                if (started != observedTick)
                {
                    observedTick = started;
                    nextStuckWarning = StuckTickWarningAfter;
                }

                if (started == 0)
                    continue;

                var running = Stopwatch.GetElapsedTime(started);
                if (running >= nextStuckWarning)
                {
                    Log.TickStuck(this.logger, (int)running.TotalSeconds, this.DescribeLevelTicks());
                    nextStuckWarning = running + StuckTickWarningInterval;
                }
            }
        }
        catch (OperationCanceledException) when (cancelTokenSource.IsCancellationRequested)
        {
            // Stopping.
        }

        // A client going back to configuration gets no keep-alives, but times out if it doesn't acknowledge it.
        static ValueTask SendKeepAliveAsync(IClient client) => client.State switch
        {
            _ when client is Client { AwaitingReconfiguration: true } awaiting
                => awaiting.CheckReconfigurationTimeoutAsync(),
            ClientState.Play => KeepAlivePacket.ClientboundPlay.HandleAsync(client),
            ClientState.Configuration => KeepAlivePacket.ClientboundConfiguration.HandleAsync(client),
            _ => ValueTask.CompletedTask
        };
    }

    /// <summary>
    /// Sends players their chunks on its own timer, so a slow or stuck world tick doesn't leave them without terrain.
    /// Chunks go out in batches the clients acknowledge (see <see cref="Player.SendPendingChunksAsync"/>).
    /// </summary>
    private async Task ChunkLoopAsync()
    {
        var timer = new BalancingTimer(50, cancelTokenSource.Token);

        try
        {
            while (await timer.WaitForNextTickAsync())
                await this.ForEachConnectionAsync(SendPendingChunksAsync, this.chunkFailures);
        }
        catch (OperationCanceledException) when (cancelTokenSource.IsCancellationRequested)
        {
            // Stopping.
        }

        static async ValueTask SendPendingChunksAsync(IClient client)
        {
            if (client.State == ClientState.Play && client.Player is Player player)
                await player.SendPendingChunksAsync();
        }
    }

    /// <summary>
    /// Runs <paramref name="service"/> for every connection. A client that fails (one closing its socket, say) is
    /// logged to <paramref name="failures"/> and skipped, so it can't hold up the clients after it.
    /// </summary>
    private async Task ForEachConnectionAsync(Func<IClient, ValueTask> service, ThrottledFailures failures)
    {
        foreach (var client in this.Connections.Values)
        {
            try
            {
                await service(client);
            }
            catch (Exception ex)
                when (ex is not OperationCanceledException || !cancelTokenSource.IsCancellationRequested)
            {
                if (failures.ShouldLog(out var suppressed))
                    Log.ConnectionTickFailed(this.logger, ex, suppressed);
            }
        }
    }

    /// <summary>Logs a recurring failure at most every ten seconds, counting the occurrences in between.</summary>
    private sealed class ThrottledFailures
    {
        private static readonly TimeSpan Interval = TimeSpan.FromSeconds(10);
        private long lastLogged;
        private int suppressed;

        public bool ShouldLog(out int suppressedSinceLast)
        {
            suppressedSinceLast = this.suppressed;
            if (this.lastLogged != 0 && Stopwatch.GetElapsedTime(this.lastLogged) < Interval)
            {
                this.suppressed++;
                return false;
            }

            this.lastLogged = Stopwatch.GetTimestamp();
            this.suppressed = 0;
            return true;
        }
    }

    // Each level's tick stage, e.g. "overworld: ticking entities, the_nether: waiting for simulation gate".
    private string DescribeLevelTicks() => string.Join(", ", this.WorldManager.GetAvailableWorlds()
        .OfType<World>()
        .SelectMany(world => world.dimensions.Values.OfType<AbstractLevel>().Prepend(world))
        .Select(level => $"{level.Name}: {level.TickStage}"));

    /// <summary>
    /// The mean time the last 100 world ticks took to run, in milliseconds, like vanilla's smoothed tick time (the busy
    /// time of a tick, not its 50 ms interval).
    /// </summary>
    public double AverageTickMilliseconds
    {
        get
        {
            lock (this.tickTimes)
                return this.tickTimeCount == 0 ? 0 : this.tickTimes.Take(this.tickTimeCount).Average();
        }
    }

    private readonly double[] tickTimes = new double[100];
    private int tickTimeCount;
    private int tickTimeIndex;

    private void RecordTickTime(TimeSpan elapsed)
    {
        lock (this.tickTimes)
        {
            this.tickTimes[this.tickTimeIndex] = elapsed.TotalMilliseconds;
            this.tickTimeIndex = (this.tickTimeIndex + 1) % this.tickTimes.Length;
            this.tickTimeCount = Math.Min(this.tickTimeCount + 1, this.tickTimes.Length);
        }
    }

    /// <summary>
    /// Ticks the worlds once and records how long it took. A failing tick is reported rather than thrown, so the loop
    /// goes on.
    /// </summary>
    private async Task TickWorldsOnceAsync()
    {
        TickStage = "ticking worlds";
        var tickStart = Stopwatch.GetTimestamp();
        Volatile.Write(ref this.tickStarted, tickStart);
        try
        {
            await this.WorldManager.TickWorldsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancelTokenSource.IsCancellationRequested)
        {
            // A failing tick used to end the tick loop for good: players could still join, but nothing ticked again.
            // The next tick runs anyway; repeated failures are only logged now and then.
            this.ReportTickFailure(ex);
        }
        finally
        {
            Volatile.Write(ref this.tickStarted, 0);
            this.RecordTickTime(Stopwatch.GetElapsedTime(tickStart));
        }
    }

    private async Task LoopAsync()
    {
        var worldTicks = 0;
        Task autosave = Task.CompletedTask;

        var tpsMeasure = new TpsMeasure();
        var stopwatch = Stopwatch.StartNew();
        var timer = new BalancingTimer(50, cancelTokenSource.Token);

        try
        {
            TickStage = "waiting for timer";
            while (await timer.WaitForNextTickAsync())
            {
                // Like vanilla, worlds tick once they're loaded: ticking chunks while the rest generate (fluids in complete
                // chunks) would change them before the world is ready.
                if (this.WorldManager.ReadyToJoin)
                {
                    // Under the gate, so a pause lands between two ticks (see PauseAsync).
                    await this.tickGate.WaitAsync();
                    try
                    {
                        if (!this.Paused)
                        {
                            await this.TickWorldsOnceAsync();

                            // The autosave runs beside the ticks, and is skipped while the previous one still runs.
                            if (++worldTicks % AutosaveInterval == 0 && autosave.IsCompleted)
                                autosave = this.SaveEverythingAsync(autosave: true);
                        }
                    }
                    finally
                    {
                        this.tickGate.Release();
                    }
                }

                long elapsedTicks = stopwatch.ElapsedTicks;
                stopwatch.Restart();
                tpsMeasure.PushMeasurement(elapsedTicks);
                Tps = tpsMeasure.Tps;
                TickStage = "waiting for timer";
            }
        }
        catch (OperationCanceledException) when (cancelTokenSource.IsCancellationRequested)
        {
            TickStage = "cancelled";
            // Just stop looping.
        }
        catch (Exception ex)
        {
            TickStage = $"failed: {ex.GetType().Name}: {ex.Message}";
            logger.LogError(ex, "The game tick loop failed");
            await this.cancelTokenSource.CancelAsync();
            throw;
        }

        await autosave;

        TickStage = "stopped";
        foreach (var client in this.Connections.Values)
        {
            await client.DisconnectAsync("Server closed");
        }

        // The worlds are saved by StopAsync, which follows.
        await this.PendingLeavesAsync();
    }

    public bool IsWhitelisted(string username) => this.WhitelistConfiguration.CurrentValue.WhitelistedPlayers.Any(x => string.Equals(x.Name, username, StringComparison.OrdinalIgnoreCase));

    public bool IsWhitelisted(Guid uuid) => this.WhitelistConfiguration.CurrentValue.WhitelistedPlayers.Any(x => x.Id == uuid);

    public async ValueTask<bool> ShouldThrottleAsync(Client client)
    {
        if (!this.Configuration.Network.ShouldThrottle)
            return false;

        if (!client.Connected)
            return false;

        if (!throttler.TryGetValue(client.Ip!, out var timeLeft))
        {
            throttler.TryAdd(client.Ip!, DateTimeOffset.UtcNow.AddMilliseconds(this.Configuration.Network.ConnectionThrottle));
            return false;
        }

        if (DateTimeOffset.UtcNow < timeLeft)
        {
            Log.Throttled(this.logger, client.Ip!);
            await client.DisconnectAsync("Connection Throttled! Please wait before reconnecting.");
            return true;
        }

        return false;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Error, Message = "Removing server-side player {Username} failed")]
        public static partial void ServerPlayerLeaveFailed(ILogger logger, Exception exception, string username);

        [LoggerMessage(Level = LogLevel.Error, Message = "A world tick failed ({Suppressed} more failures weren't logged since the last one)")]
        public static partial void TickFailed(ILogger logger, Exception exception, int suppressed);

        [LoggerMessage(Level = LogLevel.Warning, Message = "The world tick has been running for {Seconds} s; levels: {Levels}")]
        public static partial void TickStuck(ILogger logger, int seconds, string levels);

        [LoggerMessage(Level = LogLevel.Error, Message = "Sending keep-alives or chunks failed ({Suppressed} more failures weren't logged since the last one)")]
        public static partial void ConnectionTickFailed(ILogger logger, Exception exception, int suppressed);

        [LoggerMessage(Level = LogLevel.Information, Message = "Starting Obsidian {Version}")]
        public static partial void Starting(ILogger logger, string version);

        [LoggerMessage(Level = LogLevel.Error, Message = "Multiplayer debug mode can't be enabled together with online mode, since it overwrites usernames")]
        public static partial void IncompatibleDebugMode(ILogger logger);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Running in offline mode; player identities are not verified")]
        public static partial void OfflineMode(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "Server ready in {Elapsed}, listening on port {Port}")]
        public static partial void Ready(ILogger logger, TimeSpan elapsed, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Server stopped")]
        public static partial void Stopped(ILogger logger);

        [LoggerMessage(Level = LogLevel.Information, Message = "{Message}")]
        public static partial void Broadcast(ILogger logger, string message);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Saving worlds")]
        public static partial void SavingWorlds(ILogger logger);

        [LoggerMessage(Level = LogLevel.Error, Message = "Saving the worlds failed")]
        public static partial void AutosaveFailed(ILogger logger, Exception exception);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Throttled {Ip} for reconnecting too quickly")]
        public static partial void Throttled(ILogger logger, string ip);

        [LoggerMessage(Level = LogLevel.Error, Message = "Accepting a connection failed with socket error {SocketError}")]
        public static partial void AcceptFailed(ILogger logger, System.Net.Sockets.SocketError socketError);

        [LoggerMessage(Level = LogLevel.Information, Message = "Rejected {Ip}: not whitelisted")]
        public static partial void NotWhitelisted(ILogger logger, string ip);

        [LoggerMessage(Level = LogLevel.Error, Message = "Sending changed recipes to {Username} failed")]
        public static partial void RecipeSyncFailed(ILogger logger, Exception exception, string username);
    }
}
