// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Services;
using Obsidian.WorldData;
using Obsidian.WorldData.Portals;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:player")]
public sealed partial class Player : Avatar, IClientPlayer
{
    public byte CurrentContainerId { get; set; }

    public IClient Client { get; internal set; }

    public ILogger Logger => this.Client.Logger;

    public bool SendPacket(IClientboundPacket packet) => this.Client.SendPacket(packet);

    public ValueTask QueuePacketAsync(IClientboundPacket packet) => this.Client.QueuePacketAsync(packet);

    internal HashSet<IPlayer> visiblePlayers = [];

    public bool IsDragging { get; set; }
    public DraggingState DragStart { get; set; }
    public List<short> DraggedSlots { get; set; } = [];
    public int TeleportId { get; set; }

    // <summary>
    /// Which chunks the player should have loaded around them.
    /// </summary>
    public ConcurrentHashSet<long> LoadedChunks { get; internal set; } = [];

    // Unsent chunks, the current nearest-first loading pass, and the client's view. Guarded by chunkUpdates so packet
    // handlers and the level tick can't update the client's chunks at the same time.
    private readonly HashSet<long> pendingChunks = [];
    private readonly Queue<long> pendingChunkLoads = [];
    private const int MaximumChunkLoadsPerPass = 16;
    private static readonly TimeSpan ChunkLoadTimeBudget = TimeSpan.FromMilliseconds(5);
    private readonly SemaphoreSlim chunkUpdates = new(1, 1);

    // Set once the player left for reconfiguration, after which the client's next player sends its chunks.
    private bool chunksStopped;

    // Vanilla's chunk batch flow control (PlayerChunkSender): chunks go out in batches the client acknowledges with how
    // many chunks a tick it can take, so a slow connection isn't flooded and later packets (keep-alives) don't wait
    // behind megabytes of chunks. The client's rate tops up a quota every tick. One batch may be unacknowledged at first,
    // ten once the client has answered. Guarded by chunkBatchLock, since acknowledgements arrive on the network thread.
    private readonly Lock chunkBatchLock = new();
    private float desiredChunksPerTick = 9;
    private float chunkBatchQuota;
    private int unacknowledgedChunkBatches;
    private int maxUnacknowledgedChunkBatches = 1;
    private (int X, int Z)? chunkCacheCenter;
    private int chunkViewDistance;

    public string Username { get; }

    public required IServer Server { get; init; }

    public PlayerInput Input { get; set; }
    internal Mob? Vehicle { get; set; }
    internal long LastExperiencePickupTick { get; set; } = -2;

    public override bool Sneaking
    {
        get => this.Input.HasFlag(PlayerInput.Sneak);
        set
        {
            if (value)
                this.Input |= PlayerInput.Sneak;
            else
                this.Input &= ~PlayerInput.Sneak;
        }
    }


    /// <summary>
    /// The players inventory.
    /// </summary>
    public Container Inventory { get; }
    public Container EnderInventory { get; }

    /// <summary>
    /// The container the player has open. Setting it moves the player between the containers'
    /// <see cref="BaseContainer.Viewers"/> and ends any drag across slots, which vanilla keeps per menu.
    /// </summary>
    public BaseContainer? OpenedContainer
    {
        get
        {
            lock (this.menuStateGate)
                return field;
        }
        set
        {
            lock (this.menuStateGate)
            {
                if (ReferenceEquals(field, value))
                    return;

                field?.RemoveViewer(this);
                value?.AddViewer(this);
                field = value;
                this.menuSync = null;

                this.IsDragging = false;
                this.DraggedSlots.Clear();
            }
        }
    }

    public List<SkinProperty> SkinProperties { get; set; } = [];

    public Vector? LastDeathLocation { get; set; }

    public ItemStack? CarriedItem { get; set; }

    /// <summary>
    /// Keeps what the player is sent about their open window in order and current, through plugins' packet handlers.
    /// Settable so that a test can stand in for the handlers.
    /// </summary>
    internal Events.WindowSnapshots WindowSnapshots { get; set; }

    /// <summary>
    /// Keeps the player's recipe book up to date with the server's recipes.
    /// </summary>
    internal Events.RecipeBookSync RecipeBookSync { get; } = new();

    public IBlock? LastClickedBlock { get; internal set; }

    public GameMode GameMode
    {
        get => field;
        set
        {
            // Validated before it's stored, so an unknown mode throws without changing the player.
            Abilities = value switch
            {
                GameMode.Creative => PlayerAbility.CreativeMode | PlayerAbility.AllowFlying | PlayerAbility.Invulnerable,
                GameMode.Spectator => PlayerAbility.AllowFlying | PlayerAbility.Invulnerable,
                GameMode.Survival or GameMode.Adventure => PlayerAbility.None,
                _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown gamemode.")
            };
            Abilities |= this.GrantedFlight;
            field = value;
        }
    }

    public PlayerAbility Abilities { get; set; }

    public IScoreboard? CurrentScoreboard
    {
        get;

        set
        {
            if (field == value)
                return;

            field?.RemovePlayer(this.EntityId);

            value?.AddPlayer(this.EntityId);

            field = value;
        }
    }

    public bool Sleeping { get; set; }
    public bool InHorseInventory { get; set; }
    public bool Respawning { get; internal set; }
    private bool pendingRespawnChunks;
    private int respawnChunkRetryTicks;

    public short AttackTime { get; set; }
    public short DeathTime { get; set; }
    public short HurtTime { get; set; }
    public short SleepTimer { get; set; }
    internal int TimeSinceRest { get; private set; }

    public short CurrentHeldItemSlot
    {
        get
        {
            if (field < 36 || field > 44)
                field = 36;

            return field;
        }
        set
        {
            if (value is < 0 or > 8)
                throw new IndexOutOfRangeException("Value must be >= 0 or <= 8");

            field = (short)(value + 36);
        }
    }

    public int Ping => Client.Ping;
    public int FoodLevel { get; set; } = 20;
    public int FoodTickTimer { get; set; }
    public int XpLevel { get; set; }
    public int XpTotal { get; set; }
    public float XpP { get; set; }

    // Worked out from the position, so that it's right from the moment the player is placed (a player who has just
    // logged in hasn't sent a movement yet). Standing eye height; poses and scale use EyePosition.
    public double HeadY => this.Position.Y + 1.62;

    public float Absorption { get; set; }
    public float FallDistance { get; set; }
    public float FoodExhaustionLevel { get; set; }
    public float FoodSaturationLevel { get; set; } = 5;

    public Entity? LeftShoulder { get; set; }
    public Entity? RightShoulder { get; set; }

    // Properties set by Obsidian (unofficial)
    // Not sure whether these should be saved to the NBT file.
    // These could be saved under nbt tags prefixed with "obsidian_"
    // As minecraft might just ignore them.
    public Permission PlayerPermissions { get; private set; } = new Permission("root");

    public string PersistentDataFile { get; }

    public string? ClientIP => Client.Ip;

    [SetsRequiredMembers]
    internal Player(Guid uuid, string username, IClient client, IWorld world)
    {
        Uuid = uuid;
        Username = username;
        EntityId = client.Id;

        Inventory = new Container(9 * 5 + 1, InventoryType.Generic)
        {
            Owner = uuid,
            IsPlayerInventory = true
        };
        EnderInventory = new Container
        {
            Title = "Ender Chest"
        };

        Level = world;
        Type = EntityType.Player;

        PersistentDataFile = Path.Combine(ServerConstants.PersistentDataPath, $"{Uuid}.dat");

        Health = 20f;

        this.Client = client;
        this.WindowSnapshots = new(this.RunQueueHandlersAsync, this.QueueWithoutHandlers);
    }

    public ItemStack? GetHeldItem() => Inventory.GetItem(CurrentHeldItemSlot);
    public ItemStack? GetOffHandItem() => Inventory.GetItem(45);

    public ValueTask DisplayScoreboardAsync(IScoreboard scoreboard, DisplaySlot slot)//TODO implement new features
    {
        var actualBoard = (Scoreboard)scoreboard;

        if (actualBoard.Objective is null)
            throw new InvalidOperationException("You must create an objective for the scoreboard before displaying it.");

        CurrentScoreboard = actualBoard;

        // Queued in order with the scoreboard's removal, so a plugin unloading meanwhile can't leave it shown.
        actualBoard.Display(this.EntityId, slot);

        return ValueTask.CompletedTask;
    }

    // The two steps of queueing a packet, for a caller that checks what it sends in between. A connection that isn't
    // a server's (a test's, say) runs no handlers and has no queue, so its packets are sent.
    private ValueTask<bool> RunQueueHandlersAsync(IClientboundPacket packet) =>
        this.Client is Client client
            ? client.RunQueueHandlersAsync(packet)
            : ValueTask.FromResult(true);

    private void QueueWithoutHandlers(IClientboundPacket packet)
    {
        if (this.Client is Client client)
            client.QueueWithoutHandlers(packet);
        else
            this.Client.SendPacket(packet);
    }

    public async ValueTask OpenInventoryAsync(BaseContainer container)
    {
        if (!this.Alive)
            return;

        if (OpenedContainer is EnchantmentTable previousTable)
            await Obsidian.Events.MainEventHandler.ReturnEnchantingItemsAsync(this, previousTable);

        if (container is EnchantmentTable table)
        {
            container = new EnchantmentTable
            {
                BlockPosition = table.BlockPosition,
                Title = table.Title,
                CustomName = table.CustomName
            };
        }

        if (this.TryShow(container) is not byte nextId)
            return;

        await Client.QueuePacketAsync(container is MenuContainer opening
            ? this.OpenMenuPacket(opening, nextId)
            : new OpenScreenPacket(container, nextId));

        // Like vanilla's menus, a plugin's is sent whole, its slots then the player's inventory, so that clients know
        // how many slots it has before the player clicks. The packet sets the cursor too, so it always goes, with the
        // player's carried item, even for an empty container.
        List<ItemStack> contents;
        if (container is MenuContainer openedMenu)
        {
            // The opening packet can await unloading. Resolve the instance's generation before reading its slots.
            if (!ReferenceEquals(this.OpenedContainer, openedMenu)
                || this.CurrentContainerId != nextId
                || !openedMenu.TryGetOwner(out var owner))
            {
                if (this.CurrentContainerId == nextId)
                    await this.Client.QueuePacketAsync(new ContainerClosePacket { ContainerId = nextId });

                return;
            }

            var plugin = owner as PluginContainer;
            using var call = plugin?.TryEnter();
            if (plugin is not null && call is null)
            {
                await this.Client.QueuePacketAsync(new ContainerClosePacket { ContainerId = nextId });
                return;
            }

            contents = openedMenu.Concat(this.Inventory.Skip(9).Take(36)).ToList();
        }
        else
        {
            contents = container is CraftingTable { Type: InventoryType.Crafting } or EnchantmentTable
                ? container.Concat(this.Inventory.Skip(9).Take(36)).ToList()
                : container.ToList();
        }

        await Client.QueuePacketAsync(new ContainerSetContentPacket(nextId, contents)
        {
            CarriedItem = this.CarriedItem
        });

        if (container is EnchantmentTable)
            await RefreshEnchantingAsync();

        if (container is BrewingStand stand)
            await AbstractLevel.SendBrewingPropertiesAsync(this, stand);

        if (container is SmeltingContainer furnace)
            await AbstractLevel.SendFurnacePropertiesAsync(this, furnace);

        if (container is MenuContainer menu)
            await this.SendMenuOpenedAsync(menu, nextId);

        // A plugin's menu can close as it opens, when its plugin unloads, and a block entity's container can be retired
        // as it opens, when its block goes; the screen that just opened closes too.
        if (this.CurrentContainerId == nextId && container.ShowsRetired)
            await this.CloseContainerAsync();
        else if (this.CurrentContainerId == nextId && !ReferenceEquals(this.OpenedContainer, container))
            await this.Client.QueuePacketAsync(new ContainerClosePacket { ContainerId = nextId });
    }

    /// <summary>
    /// Makes <paramref name="container"/> the open container and numbers its window, unless it's a menu whose type
    /// doesn't open now (see <see cref="MenuDefinition.IsOpenable"/>), or that the player's client doesn't know (see
    /// <see cref="KnowsMenu"/>).
    /// </summary>
    /// <returns>The window's id, or null when the menu doesn't open.</returns>
    private byte? TryShow(BaseContainer container)
    {
        // A container whose block entity is gone doesn't open again.
        if (container.ShowsRetired)
            return null;

        if (container is not MenuContainer menu)
            return Show();

        lock (menu.Menu.Gate)
            return menu.TryGetOwner(out _) && this.KnowsMenu(menu.Menu) ? Show() : null;

        byte Show()
        {
            this.OpenedContainer = container;

            return this.GetNextContainerId();
        }
    }

    /// <summary>
    /// Closes the player's open container from the server, like the player closing it: the close event runs, then
    /// their screen closes. Unlike the player's close, cancelling the event doesn't keep it open.
    /// </summary>
    /// <param name="unloading">
    /// The plugin whose unloading closes the container. Its handlers of the close event still run, although it no
    /// longer takes other calls.
    /// </param>
    internal async ValueTask CloseContainerAsync(PluginContainer? unloading = null)
    {
        // Death owns mandatory window removal. A concurrent close must not return its transient inputs to inventory.
        if (!this.Alive)
            return;

        if (this.OpenedContainer is not BaseContainer container)
            return;

        var containerId = this.CurrentContainerId;
        var closed = new ContainerClosedEventArgs(this, this.Server, container, containerId);

        if (unloading is not null && this.EventDispatcher is EventDispatcher dispatcher)
            await dispatcher.ExecuteEventAsync(closed, unloading);
        else
            await this.EventDispatcher.ExecuteEventAsync(closed);

        // A handler may have opened another container instead, which stays open. The server's own handler has
        // usually let go of the container already, so the window's id tells.
        if (this.CurrentContainerId != containerId)
            return;

        if (ReferenceEquals(this.OpenedContainer, container))
            this.OpenedContainer = null;

        await this.Client.QueuePacketAsync(new ContainerClosePacket { ContainerId = containerId });
    }

    // Vanilla's creative drop throttle: each drop counts 20, each tick (50 ms here) takes one off, and drops stop
    // from 1480 on. Only the player's own packets, which are handled in turn, count drops.
    private const int CreativeDropCost = 20;
    private const int CreativeDropLimit = 1480;

    private int creativeDrops;
    private long creativeDropsTick;

    /// <summary>
    /// Counts a drop from the creative inventory screen, unless the player is dropping too fast.
    /// </summary>
    /// <returns>Whether the drop happens.</returns>
    internal bool CountCreativeDrop()
    {
        var tick = Environment.TickCount64 / 50;
        this.creativeDrops = (int)Math.Max(0, this.creativeDrops - (tick - this.creativeDropsTick));
        this.creativeDropsTick = tick;

        if (this.creativeDrops >= CreativeDropLimit)
            return false;

        this.creativeDrops += CreativeDropCost;

        return true;
    }

    public override ValueTask TeleportAsync(VectorD pos) => this.TeleportAsync(this.Level, pos);

    private async ValueTask TeleportWithinLevelAsync(VectorD pos)
    {
        FallDistance = 0;
        combatPosition = null;
        CancelWeaponUse();
        Vehicle?.Dismount(this);
        LastPosition = Position;
        if (Level is AbstractLevel resident && !resident.TryMoveEntity(this, Position, pos))
            throw new InvalidOperationException("The destination region could not accept the player.");

        Position = pos;
        BoundingBox = Dimension.CreateBBFromPosition(pos);
        await UpdateChunksAsync();

        var tid = Globals.Random.Next(0, 999);

        await EventDispatcher.ExecuteEventAsync(
            new PlayerTeleportEventArgs
            (
                this,
                this.Server,
                LastPosition,
                pos
            ));

        await Client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = pos,
            Delta = Motion,
            Yaw = Yaw,
            Pitch = Pitch,
            TeleportId = tid
        });
        TeleportId = tid;
    }

    public override ValueTask TeleportAsync(IEntity to) =>
        this.TeleportAsync(to.Level, to.Position, to.Yaw, to.Pitch);

    public override ValueTask TeleportAsync(IWorld world) =>
        this.TeleportAsync(world, world.LevelData.SpawnPosition);

    public override async ValueTask TeleportAsync(
        ILevel level, VectorD position, Angle? yaw = null, Angle? pitch = null)
    {
        ValidateTravel(level, position, yaw, pitch);
        if (!this.Alive || this.Respawning)
            throw new InvalidOperationException("A dead or respawning player cannot travel.");

        await this.CancelPortalTravelAsync();
        if (this.Level != level)
        {
            await this.TransferDimensionAsync(level, position, yaw, pitch);
            return;
        }

        var source = this.Level;
        using var area = level is AbstractLevel resident
            ? await PortalArea.LoadAsync(resident, (Vector)position.Floor(), 0,
                () => this.Client.Connected && this.Alive && this.Level == source)
            : null;
        if (level is AbstractLevel && area is null)
            throw new InvalidOperationException("The player moved while the destination was loading.");

        this.Yaw = yaw ?? this.Yaw;
        this.Pitch = pitch ?? this.Pitch;
        await this.TeleportWithinLevelAsync(position);
    }

    public ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature) =>
        throw new NotImplementedException();

    public ValueTask SendMessageAsync(ChatMessage message) =>
        Client.QueuePacketAsync(new SystemChatPacket(message, false));

    public ValueTask SetActionBarTextAsync(ChatMessage message) =>
        Client.QueuePacketAsync(new SystemChatPacket(message, true));

    public async ValueTask SendSoundAsync(ISoundEffect soundEffect)
    {
        ClientboundPacket packet = soundEffect.SoundPosition is SoundPosition soundPosition ?
            new SoundPacket
            {
                SoundLocation = soundEffect.SoundId,
                SoundPosition = soundPosition,
                Category = soundEffect.SoundCategory,
                Volume = soundEffect.Volume,
                Pitch = soundEffect.Pitch,
                Seed = soundEffect.Seed,
                FixedRange = soundEffect.FixedRange
            }
            :
            new SoundEntityPacket
            {
                SoundLocation = soundEffect.SoundId,
                EntityId = soundEffect.EntityId!.Value,
                Category = soundEffect.SoundCategory,
                Volume = soundEffect.Volume,
                Pitch = soundEffect.Pitch,
                Seed = soundEffect.Seed,
                FixedRange = soundEffect.FixedRange
            };

        await Client.QueuePacketAsync(packet);
    }

    public async ValueTask KickAsync(string reason) => await this.Client.DisconnectAsync(reason);
    public async ValueTask KickAsync(ChatMessage reason) => await Client.DisconnectAsync(reason);

    public async Task<bool> TeleportToDimensionAsync(string dimension)
    {
        var world = this.Level is IDimension child ? child.ParentWorld as World : this.Level as World;
        ILevel? destination = world?.DimensionName == dimension
            ? world
            : world?.dimensions.GetValueOrDefault(dimension);

        if (destination is null || !this.Alive || this.Respawning)
            return false;

        if (destination == this.Level)
            await this.TeleportAsync(destination.LevelData.SpawnPosition);
        else
            await this.TransferDimensionAsync(destination);

        return true;
    }

    internal async Task TransferDimensionAsync(
        ILevel destination, VectorD? position = null, Angle? yaw = null, Angle? pitch = null,
        VectorD? motion = null, bool portal = false, bool relativeMotion = false)
    {
        await this.CancelPortalTravelAsync();
        CancelEating();
        CancelWeaponUse();
        combatPosition = null;
        Vehicle?.Dismount(this);
        var origin = Level;
        var from = this.Position;
        var spawn = position ?? destination.LevelData.SpawnPosition;
        ValidateTravel(destination, spawn, yaw, pitch);
        using var area = destination is AbstractLevel target
            ? await PortalArea.LoadAsync(target, (Vector)spawn.Floor(), 0,
                () => this.Client.Connected && this.Alive && this.IsTravelResident(origin))
            : null;
        if (destination is AbstractLevel && area is null)
            return;

        await this.CloseContainerAsync();
        // Held from the level change until the respawn resets the client's view (see RespawnUnderLockAsync).
        await this.chunkUpdates.WaitAsync();
        try
        {
            this.CommitTravel(origin, destination, spawn, yaw, pitch);
            foreach (var observer in origin.GetPlayersInRange(from, float.MaxValue).OfType<Player>())
                observer.visiblePlayers.Remove(this);

            Motion = motion ?? Motion;
            FallDistance = 0;
            foodPosition = null;
            this.preservePortalMotion = relativeMotion;
            if (portal)
                this.portalCooldown = PlayerPortalCooldown;

            await this.RespawnUnderLockAsync(DataKept.Attributes | DataKept.Metadata, levelChange: true);
        }
        finally
        {
            this.preservePortalMotion = false;
            this.chunkUpdates.Release();
        }

        // Vanilla 1.21.11 sends the shared world's GlobalPos spawn, including while in a child dimension.
        var sharedSpawn = destination is IDimension child ? child.ParentWorld : destination;
        await Client.QueuePacketAsync(new SetTimePacket(destination.LevelData.Time, destination.LevelData.DayTime, true));
        await Client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(new()
        {
            Dimension = sharedSpawn.DimensionName,
            Pos = (Vector)sharedSpawn.LevelData.SpawnPosition.Floor()
        }, 0, 0));
        var weather = destination.LevelData.Raining
            ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining;
        await Client.QueuePacketAsync(new GameEventPacket(weather));
        foreach (var (id, effect) in ActivePotionEffects)
            await Client.QueuePacketAsync(new UpdateMobEffectPacket(EntityId, id, effect.CurrentDuration)
            {
                Amplifier = effect.EffectData.Amplifier,
                Flags = EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon
            });
        await Client.QueuePacketAsync(new GameEventPacket(
            ChangeGameStateReason.RainLevelChange, destination.LevelData.Raining ? 1 : 0));
        await Client.QueuePacketAsync(new GameEventPacket(
            ChangeGameStateReason.ThunderLevelChange, destination.LevelData.Thundering ? 1 : 0));
        await Client.QueuePacketAsync(new SetExperiencePacket(this.XpP, this.XpLevel, this.XpTotal));
        await Client.QueuePacketAsync(new PlayerAbilitiesPacket { Abilities = this.Abilities });
        await this.EventDispatcher.ExecuteEventAsync(new PlayerTeleportEventArgs(this, this.Server, from, spawn));
        await this.FireProgressAsync("changed_dimension", from: origin.DimensionName, to: destination.DimensionName);
        this.progressDimension = destination.DimensionName;
        await SaveAsync();
    }

    public Task RespawnAsync(DataKept dataKept = DataKept.Metadata) =>
        DeferredInventoryWork.RunOrDeferAsync(() => new ValueTask(this.RespawnAfterDeathAsync(dataKept))).AsTask();

    private async Task RespawnAfterDeathAsync(DataKept dataKept)
    {
        if (Respawning)
            return;

        while (true)
        {
            await this.WaitForDeathAsync();
            await using var callbacks = DeferredInventoryWork.Begin(this.Logger);
            await this.chunkUpdates.WaitAsync();
            try
            {
                if (await this.RespawnUnderLockAsync(dataKept))
                    return;
            }
            finally
            {
                this.chunkUpdates.Release();
            }
        }
    }

    /// <summary>
    /// Respawns the player, with <see cref="chunkUpdates"/> already held. Callers that change the player's level take
    /// it before the change, so the chunk loop can't send chunks of the old level after the respawn packet, or load
    /// the new level's chunks into the old view.
    /// </summary>
    private async Task<bool> RespawnUnderLockAsync(DataKept dataKept, bool levelChange = false)
    {
        lock (this.deathState)
        {
            if (this.Respawning)
                return true;

            // Death may have started while this caller waited for chunkUpdates. Retry outside that lock.
            if (this.deathFinished is { Task.IsCompleted: false })
                return false;

            this.Respawning = true;
            this.respawnFinished = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        this.ResetAttachmentSync();
        this.ResetAttributeSync();
        var died = !Alive;
        try
        {
            if (died)
            {
                // Death respawns must create fresh client state rather than retain the dead player's metadata.
                dataKept = DataKept.None;
                // Death completed before taking chunkUpdates; never drain items again during respawn.
                API.Data.AttachmentStorage.For(this).Respawn(this);
                CancelEating();
                CancelWeaponUse();
                combatPosition = null;
                attackTicks = 0;
                Vehicle?.Dismount(this);
                ClearPotionEffects();
                damageCooldown = 0;
                lastIncomingDamage = 0;
                HurtTime = 0;
                DeathTime = 0;
                FireTicks = 0;
                Burning = false;
                Motion = VectorD.Zero;
                FallDistance = 0;
                Absorption = 0;
                AbsorbtionAmount = 0;
                FoodLevel = 20;
                FoodSaturationLevel = 5;
                FoodExhaustionLevel = 0;
                FoodTickTimer = 0;
                foodPosition = null;
                Sprinting = false;
                Swimming = false;
                Sleeping = false;
                Pose = Pose.Standing;
                Yaw = 0;
                Pitch = 0;
                MovementFlags = MovementFlags.None;
                Health = MaxHealth;
                this.progressDead = false;
                API.Data.AttachmentStorage.For(this).EndDeath();
                await Level.DestroyEntityAsync(this);
                Position = Level.LevelData.SpawnPosition;
                LastPosition = Position;
                BoundingBox = Dimension.CreateBBFromPosition(Position);
                Level.TryAddEntity(this);
            }

            var codec = Level.DimensionType;

            Log.ChangingLevel(this.Logger, this.Username, this.Level.Name);

            await Client.QueuePacketAsync(new RespawnPacket
            {
                CommonPlayerSpawnInfo = new()
                {
                    DimensionType = codec.Id,
                    DimensionName = Level.DimensionName,
                    GameMode = GameMode,
                    PreviousGamemode = GameMode,
                    HashedSeed = 0,
                    Flat = false,
                    Debug = false,
                    PortalCooldown = this.portalCooldown,
                },
                DataKept = dataKept,
            });

            // Vanilla's level transfer keeps metadata and attributes, then sends the target difficulty.
            if (levelChange)
                await Client.QueuePacketAsync(ChangeDifficultyPacket.Of(Level.LevelData));

            visiblePlayers.Clear();

            TeleportId = 0;

            await Client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks));
            await Client.QueuePacketAsync(new PlayerPositionPacket
            {
                Position = Position,
                Yaw = Yaw,
                Pitch = Pitch,
                Delta = this.preservePortalMotion ? VectorD.Zero : Motion,
                Flags = 0,
                RelativeFlags = this.preservePortalMotion ? NetherPortalRelativeFlags : 0,
                TeleportId = 0
            });

            pendingRespawnChunks = !await this.UpdateChunksUnderLockAsync(true);
            respawnChunkRetryTicks = 20;
            Level.TryAddEntity(this);
            await Client.QueuePacketAsync(new SetHealthPacket(Health, FoodLevel, FoodSaturationLevel));
            await SendPlayerInfoAsync();

            // Vanilla's respawn sends the experience the new player starts with.
            if (died)
                await Client.QueuePacketAsync(new SetExperiencePacket(this.XpP, this.XpLevel, this.XpTotal));

            await TrySpawnPlayerAsync(Position);
            await SynchronizeTrackedEntitiesAsync();
        }
        finally
        {
            lock (this.deathState)
            {
                this.Respawning = false;
                this.respawnFinished!.TrySetResult();
            }
        }

        return true;
    }

    //TODO make IDamageSource 
    public override ValueTask KillAsync(IEntity source, ChatMessage deathMessage) =>
        this.BeginDeathAsync(source, deathMessage);

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(17, EntityMetadataType.Float);
        writer.WriteSingle(Absorption);

        writer.WriteEntityMetadataType(18, EntityMetadataType.VarInt);
        writer.WriteVarInt(XpTotal);

        this.MetadataIndex = 19;

        //TODO fix possibly an extension method?
        if (this.LeftShoulder is not null)
        {
            writer.WriteEntityMetadataType(19, EntityMetadataType.OptionalLivingEntityReference);
            writer.WriteNbtCompound([]);
            this.MetadataIndex = 20;
        }

        if (this.RightShoulder is not null)
        {
            writer.WriteEntityMetadataType(20, EntityMetadataType.OptionalLivingEntityReference);
            writer.WriteNbtCompound([]);
            this.MetadataIndex = 21;
        }
    }

    public async ValueTask SetGamemodeAsync(GameMode gamemode)
    {
        // Set first: the setter rejects unknown modes, and they must not reach clients.
        GameMode = gamemode;

        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new PlayerInfoUpdatePacket(CompilePlayerInfo(new UpdateGamemodeInfoAction(gamemode))));

        await Client.QueuePacketAsync(new GameEventPacket(gamemode));
    }

    public ValueTask UpdateDisplayNameAsync(string newDisplayName)
    {
        this.PacketBroadcaster.QueuePacketToLevel(this.Level,
            new PlayerInfoUpdatePacket(CompilePlayerInfo(new UpdateDisplayNameInfoAction(newDisplayName))));

        CustomName = newDisplayName;

        return default;
    }

    public async ValueTask SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new SetTitleTextPacket
        {
            Text = title
        };

        var titleTimesPacket = new SetTitlesAnimationPacket
        {
            FadeIn = fadeIn,
            FadeOut = fadeOut,
            Stay = stay,
        };

        await Client.QueuePacketAsync(titlePacket);
        await Client.QueuePacketAsync(titleTimesPacket);
    }

    public async ValueTask SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new SetSubtitleTextPacket
        {
            Text = subtitle
        };

        await Client.QueuePacketAsync(titlePacket);

        await SendTitleAsync(title, fadeIn, stay, fadeOut);
    }

    public async ValueTask SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new SetSubtitleTextPacket
        {
            Text = subtitle
        };

        var titleTimesPacket = new SetTitlesAnimationPacket
        {
            FadeIn = fadeIn,
            FadeOut = fadeOut,
            Stay = stay,
        };

        await Client.QueuePacketAsync(titlePacket);
        await Client.QueuePacketAsync(titleTimesPacket);
    }

    public async ValueTask SendActionBarAsync(string text)
    {
        var actionBarPacket = new SetActionBarTextPacket
        {
            Text = text
        };

        await Client.QueuePacketAsync(actionBarPacket);
    }

    //TODO 
    public ValueTask SpawnParticleAsync(ParticleData data) => throw new NotImplementedException();

    public async Task<bool> GrantPermissionAsync(string permissionNode)
    {
        var permissions = permissionNode.ToLower().Trim().Split('.');

        var parent = PlayerPermissions;
        var result = false;

        foreach (var permission in permissions)
        {
            // no such child, this permission is new!
            if (!parent.Children.Any(x => x.Name.EqualsIgnoreCase(permission)))
            {
                // create the new child, add it to its parent and set parent to the next value to continue the loop
                var child = new Permission(permission);
                parent.Children.Add(child);
                parent = child;
                // yes, new permission!
                result = true;
                continue;
            }

            // child already exists, set parent to existing child to continue loop
            parent = parent.Children.First(x => x.Name.EqualsIgnoreCase(permission));
        }

        await SavePermsAsync();

        if (result)
            await this.EventDispatcher.ExecuteEventAsync(new PermissionGrantedEventArgs(this, this.Server, permissionNode));

        return result;
    }

    public async Task<bool> RevokePermissionAsync(string permissionNode)
    {
        var permissions = permissionNode.ToLower().Trim().Split('.');

        // Set root node and whether we created a new permission (still false)
        var parent = PlayerPermissions;

        foreach (var permission in permissions)
        {
            if (parent.Children.Any(x => x.Name.EqualsIgnoreCase(permission)))
            {
                // child exists remove them
                var childToRemove = parent.Children.First(x => x.Name.EqualsIgnoreCase(permission));

                parent.Children.Remove(childToRemove);

                await this.SavePermsAsync();
                await this.Server.EventDispatcher.ExecuteEventAsync(new PermissionRevokedEventArgs(this, this.Server, permissionNode));

                return true;
            }
        }

        return false;
    }

    public bool HasPermission(string permissionNode)
    {
        var parent = PlayerPermissions;
        if (parent.Children.Count == 0)
            return false;

        var permissions = permissionNode.ToLower().Trim().Split('.');

        foreach (var permission in permissions)
        {
            if (parent.Children.Any(x => x.Name == Permission.Wildcard) || parent.Children.Any(x => x.Name.EqualsIgnoreCase(permission)))
                return true;

            parent = parent.Children.First(x => x.Name.EqualsIgnoreCase(permission));
        }

        return false;
    }

    public bool HasAnyPermission(IEnumerable<string> permissions) => permissions.Any(x => HasPermission(x));

    public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.All(x => HasPermission(x));

    public byte GetNextContainerId()
    {
        CurrentContainerId = (byte)(CurrentContainerId % 255 + 1);

        return CurrentContainerId;
    }

    public override string ToString() => Username;

    public async override ValueTask UpdateAsync(VectorD position, MovementFlags movementFlags)
    {
        if (!Alive || Respawning) return;
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z)) return;
        if (Vehicle != null)
            position = Position;
        var oldPosition = Position;
        var oldMovementFlags = MovementFlags;
        await base.UpdateAsync(position, movementFlags);
        this.RecordProgressMovement(oldPosition, oldMovementFlags);
        await UpdateFallAsync(oldPosition, movementFlags);
        if (Level is AbstractLevel events) await events.EmitMovementGameEventsAsync(this, oldPosition, oldMovementFlags);

        await TrySpawnPlayerAsync(position);
    }

    public async override ValueTask UpdateAsync(VectorD position, Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        if (!Alive || Respawning) return;
        if (!double.IsFinite(position.X) || !double.IsFinite(position.Y) || !double.IsFinite(position.Z)) return;
        if (Vehicle != null)
            position = Position;
        var oldPosition = Position;
        var oldMovementFlags = MovementFlags;
        await base.UpdateAsync(position, yaw, pitch, movementFlags);
        this.RecordProgressMovement(oldPosition, oldMovementFlags);
        await UpdateFallAsync(oldPosition, movementFlags);
        if (Level is AbstractLevel events) await events.EmitMovementGameEventsAsync(this, oldPosition, oldMovementFlags);

        await TrySpawnPlayerAsync(position);
    }

    public async override ValueTask UpdateAsync(Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        if (!Alive || Respawning) return;
        await base.UpdateAsync(yaw, pitch, movementFlags);
        MovementFlags = movementFlags;
        await UpdateFallAsync(Position, movementFlags);
    }

    public async ValueTask DisconnectAsync(ChatMessage reason) => await this.Client.DisconnectAsync(reason);

    /// <summary>
    /// Updates the chunks the client has to its view around the player, like vanilla's chunk tracking: the client learns
    /// the new center, chunks out of view are forgotten, and chunks in view are sent nearest first. Chunks that aren't
    /// generated yet are queued for generation and sent once they are (see <see cref="SendPendingChunksAsync"/>).
    /// </summary>
    /// <param name="unloadAll">Whether the client starts over without chunks (it changed level or respawned).</param>
    /// <param name="distance">The view distance, instead of the client's.</param>
    /// <returns>Whether every chunk in view has been sent.</returns>
    public async Task<bool> UpdateChunksAsync(bool unloadAll = false, int distance = 0)
    {
        await this.chunkUpdates.WaitAsync();
        try
        {
            return await this.UpdateChunksUnderLockAsync(unloadAll, distance);
        }
        finally
        {
            this.chunkUpdates.Release();
        }
    }

    // UpdateChunksAsync, with chunkUpdates already held.
    private async Task<bool> UpdateChunksUnderLockAsync(bool unloadAll, int distance = 0)
    {
        if (unloadAll)
        {
            var tracked = TrackedEntities.Keys.ToArray();
            TrackedEntities.Clear();
            if (!Respawning && tracked.Length > 0)
                await Client.QueuePacketAsync(new RemoveEntitiesPacket(tracked));
            if (!Respawning)
            {
                foreach (var value in LoadedChunks)
                {
                    NumericsHelper.LongToInts(value, out var x, out var z);
                    await UnloadChunkAsync(x, z);
                }
            }

            LoadedChunks.Clear();
            this.pendingChunks.Clear();
            this.pendingChunkLoads.Clear();
            this.chunkCacheCenter = null;
        }

        var (centerX, centerZ) = Position.ToChunkCoord();

        // The client drops chunks outside the range around its center, so the center goes first.
        if (this.chunkCacheCenter != (centerX, centerZ))
        {
            this.chunkCacheCenter = (centerX, centerZ);
            this.pendingChunkLoads.Clear();
            await Client.QueuePacketAsync(new SetChunkCacheCenterPacket(centerX, centerZ));
        }

        // Like vanilla, at least 2.
        var viewDistance = Math.Max(2, distance < 1 ? ClientInformation.ViewDistance : distance);
        if (this.chunkViewDistance != viewDistance)
            this.pendingChunkLoads.Clear();
        this.chunkViewDistance = viewDistance;

        foreach (var value in LoadedChunks)
        {
            NumericsHelper.LongToInts(value, out var x, out var z);
            if (!this.IsInView(x, z) && LoadedChunks.TryRemove(value))
                await Client.QueuePacketAsync(new ForgetLevelChunkPacket(x, z));
        }

        this.pendingChunks.RemoveWhere(value =>
        {
            NumericsHelper.LongToInts(value, out var x, out var z);
            return !this.IsInView(x, z);
        });

        for (var x = centerX - this.chunkViewDistance - 1; x <= centerX + this.chunkViewDistance + 1; x++)
        {
            for (var z = centerZ - this.chunkViewDistance - 1; z <= centerZ + this.chunkViewDistance + 1; z++)
            {
                var value = NumericsHelper.IntsToLong(x, z);
                if (this.IsInView(x, z) && !LoadedChunks.Contains(value))
                    this.pendingChunks.Add(value);
            }
        }

        return await this.SendReadyChunksAsync();
    }

    /// <summary>
    /// Sends the chunks in view that weren't generated when they were last asked for and are now. The server's chunk
    /// loop calls it every tick, independently of the world tick; it skips the tick while the chunks are being updated.
    /// </summary>
    /// <summary>
    /// Waits for a chunk send under way and stops sending this player chunks: they left to go back through
    /// configuration, and the client's next player gets its chunks.
    /// </summary>
    internal async Task StopSendingChunksAsync()
    {
        await this.chunkUpdates.WaitAsync();
        this.chunksStopped = true;
        this.chunkUpdates.Release();
    }

    internal async Task SendPendingChunksAsync()
    {
        lock (this.chunkBatchLock)
        {
            if (this.unacknowledgedChunkBatches < this.maxUnacknowledgedChunkBatches)
            {
                var cap = Math.Max(1, this.desiredChunksPerTick);
                this.chunkBatchQuota = Math.Min(this.chunkBatchQuota + this.desiredChunksPerTick, cap);
            }
        }

        if (this.pendingChunks.Count == 0 || !await this.chunkUpdates.WaitAsync(0))
            return;

        try
        {
            await this.SendReadyChunksAsync();
        }
        catch (OperationCanceledException)
        {
            // The client disconnected while chunks were queued for it, which shouldn't reach the level's tick: the server
            // loop takes a cancellation for its own shutdown.
        }
        finally
        {
            this.chunkUpdates.Release();
        }
    }

    // Bound disk reads and packet queuing so login and movement don't wait for the entire view. Finish a pass before
    // retrying ungenerated chunks, so they can't prevent the rest of the view from being requested. Called under chunkUpdates.
    private async Task<bool> SendReadyChunksAsync()
    {
        if (this.chunksStopped)
            return true;

        if (this.pendingChunks.Count == 0 || this.chunkCacheCenter is not var (centerX, centerZ))
            return this.pendingChunks.Count == 0;

        if (this.pendingChunkLoads.Count == 0)
        {
            var chunks = this.pendingChunks.ToArray();
            var distances = new int[chunks.Length];
            for (var i = 0; i < chunks.Length; i++)
            {
                NumericsHelper.LongToInts(chunks[i], out var x, out var z);
                distances[i] = (x - centerX) * (x - centerX) + (z - centerZ) * (z - centerZ);
            }

            Array.Sort(distances, chunks);
            foreach (var value in chunks)
                this.pendingChunkLoads.Enqueue(value);
        }

        int allowed;
        lock (this.chunkBatchLock)
        {
            allowed = this.unacknowledgedChunkBatches < this.maxUnacknowledgedChunkBatches
                ? (int)Math.Floor(this.chunkBatchQuota)
                : 0;
        }

        if (allowed <= 0)
            return false;

        var batch = new List<IChunk>();
        var started = Stopwatch.GetTimestamp();
        for (var count = 0;
             count < MaximumChunkLoadsPerPass && batch.Count < allowed && this.pendingChunkLoads.TryDequeue(out var value);
             count++)
        {
            if (this.pendingChunks.Contains(value))
            {
                NumericsHelper.LongToInts(value, out var x, out var z);
                if (await Level.GetChunkAsync(x, z) is IChunk { IsGenerated: true } chunk)
                {
                    batch.Add(chunk);
                    LoadedChunks.Add(value);
                    this.pendingChunks.Remove(value);
                }
            }

            if (Stopwatch.GetElapsedTime(started) >= ChunkLoadTimeBudget)
                break;
        }

        if (batch.Count > 0)
        {
            lock (this.chunkBatchLock)
            {
                this.unacknowledgedChunkBatches++;
                this.chunkBatchQuota -= batch.Count;
            }

            await Client.QueuePacketAsync(new ChunkBatchStartPacket());
            foreach (var chunk in batch)
                await Client.QueuePacketAsync(new LevelChunkWithLightPacket(chunk));

            await Client.QueuePacketAsync(new ChunkBatchFinishedPacket { BatchSize = batch.Count });
        }

        return this.pendingChunks.Count == 0;
    }

    /// <summary>
    /// Takes the client's acknowledgement of a chunk batch and the chunks a tick it asks for (vanilla's
    /// <c>onChunkBatchReceivedByClient</c>).
    /// </summary>
    internal void OnChunkBatchReceived(float chunksPerTick)
    {
        lock (this.chunkBatchLock)
        {
            this.unacknowledgedChunkBatches = Math.Max(0, this.unacknowledgedChunkBatches - 1);
            this.desiredChunksPerTick = float.IsNaN(chunksPerTick) ? .01f : Math.Clamp(chunksPerTick, .01f, 64);
            if (this.unacknowledgedChunkBatches == 0)
                this.chunkBatchQuota = 1;

            this.maxUnacknowledgedChunkBatches = 10;
        }
    }

    // Vanilla's ChunkTrackingView.contains, which counts the neighbors the client needs to render the edge: a cylinder
    // around the center that reaches one chunk past the view distance along the axes.
    private bool IsInView(int chunkX, int chunkZ)
    {
        if (this.chunkCacheCenter is not var (centerX, centerZ))
            return false;

        long dx = Math.Max(0, Math.Abs(chunkX - centerX) - 2);
        long dz = Math.Max(0, Math.Abs(chunkZ - centerZ) - 2);
        return dx * dx + dz * dz < (long)this.chunkViewDistance * this.chunkViewDistance;
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Moving {Username} to {LevelName}")]
        public static partial void ChangingLevel(ILogger logger, string username, string levelName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Username} was saved in {Dimension}; spawning them at the world spawn")]
        public static partial void OtherDimension(ILogger logger, string username, string dimension);
    }
}
