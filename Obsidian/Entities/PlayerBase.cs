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
using System.IO;
using System.Threading;

namespace Obsidian.Entities;

/// <summary>
/// What every player shares, whether a client is connected to it (<see cref="Player"/>) or not
/// (<see cref="ServerPlayer"/>): their state and gameplay, such as hunger, combat, items, riding, death and travel.
/// What a player is shown goes through <see cref="SendPacket"/> and <see cref="QueuePacketAsync"/>, which a player
/// without a client ignores; the connection's own work (chunk streaming, entity tracking) is in <see cref="Player"/>.
/// </summary>
[MinecraftEntity("minecraft:player")]
public abstract partial class PlayerBase : Avatar, IPlayer
{
    public byte CurrentContainerId { get; set; }

    public abstract ILogger Logger { get; }

    public abstract bool SendPacket(IClientboundPacket packet);

    public abstract ValueTask QueuePacketAsync(IClientboundPacket packet);

    /// <summary>
    /// The player's client, or null for a player without one.
    /// </summary>
    internal virtual IClient? Connection => null;

    /// <summary>
    /// Whether the player is still in the game: their client is connected, or a server-side player wasn't removed.
    /// Waits that span ticks, such as loading a travel's destination, stop once it's false.
    /// </summary>
    internal abstract bool Connected { get; }

    public abstract int Ping { get; }

    public abstract string? ClientIP { get; }

    public abstract ValueTask KickAsync(string reason);
    public abstract ValueTask KickAsync(ChatMessage reason);
    public abstract ValueTask DisconnectAsync(ChatMessage reason);

    public bool IsDragging { get; set; }
    public DraggingState DragStart { get; set; }
    public List<short> DraggedSlots { get; set; } = [];
    public int TeleportId { get; set; }

    // <summary>
    /// Which chunks the player should have loaded around them.
    /// </summary>
    public ConcurrentHashSet<long> LoadedChunks { get; internal set; } = [];

    // Held while the player's position, level and view change together: teleports, level changes, respawns and chunk
    // updates.
    private protected readonly SemaphoreSlim chunkUpdates = new(1, 1);

    public string Username { get; }

    public required IServer Server { get; init; }

    public PlayerInput Input { get; set; }
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

                if (field is MenuContainer closing)
                    this.RecordMenuClosed(closing);

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
    /// Settable so that a test can stand in for the handlers; the click pipeline finds them through
    /// <see cref="Events.WindowSnapshots.Of"/>.
    /// </summary>
    internal Events.WindowSnapshots WindowSnapshots
    {
        get => this.windowSnapshots;
        set => this.windowSnapshots = value.For(this);
    }

    private Events.WindowSnapshots windowSnapshots = null!;

    /// <summary>
    /// Keeps the player's recipe book up to date with the server's recipes.
    /// </summary>
    internal Events.RecipeBookSync RecipeBookSync { get; } = new();

    async ValueTask IPlayer.SynchronizeRecipeBookAsync()
    {
        if (await this.RecipeBookSync.SendChangesAsync(this))
            await Events.MainEventHandler.RefreshCraftingResultAsync(this);
    }

    ValueTask IPlayer.RefreshWindowAsync() =>
        Events.MainEventHandler.RefreshWindowAsync(this, this.OpenedContainer ?? this.Inventory, stateId: 0);


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

    internal override void ResetFallDistance() => this.FallDistance = 0;
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

    private protected PlayerBase(Guid uuid, string username, int entityId, IWorld world)
    {
        Uuid = uuid;
        Username = username;
        EntityId = entityId;

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

        this.WindowSnapshots = new(this.RunQueueHandlersAsync, this.SendInTurn, this.RunInTurnAsync);
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

    // What the window snapshots send through. Without a client's pipeline (see Player), no handlers run: the checks
    // run at once and the packets are sent.
    private protected virtual ValueTask<bool> RunQueueHandlersAsync(IClientboundPacket packet) =>
        ValueTask.FromResult(true);

    private protected virtual void SendInTurn(IClientboundPacket packet) => this.SendPacket(packet);

    private protected virtual ValueTask RunInTurnAsync(Func<ValueTask> work) => work();

    public async ValueTask OpenInventoryAsync(BaseContainer container)
    {
        if (!this.Alive)
            return;

        if (OpenedContainer is MerchantContainer previousMerchant)
            await previousMerchant.CloseAsync(this, notify: false);

        if (OpenedContainer is EnchantmentTable previousTable)
            await Obsidian.Events.MainEventHandler.ReturnEnchantingItemsAsync(this, previousTable);
        if (Obsidian.Events.MainEventHandler.HoldsItemsWhileOpen(OpenedContainer))
            await Obsidian.Events.MainEventHandler.ReturnWorkstationItemsAsync(this, OpenedContainer!);

        if (container is EnchantmentTable table)
        {
            container = new EnchantmentTable
            {
                BlockPosition = table.BlockPosition,
                Title = table.Title,
                CustomName = table.CustomName
            };
        }

        var shown = this.TryShow(container);

        // The menu it opened over closed.
        this.RunMenusClosed();
        if (shown is not byte nextId
            || !ReferenceEquals(this.OpenedContainer, container)
            || this.CurrentContainerId != nextId)
        {
            return;
        }

        await this.QueuePacketAsync(container is MenuContainer opening
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
                    await this.QueuePacketAsync(new ContainerClosePacket { ContainerId = nextId });

                return;
            }

            var plugin = owner as PluginContainer;
            using var call = plugin?.TryEnter();
            if (plugin is not null && call is null)
            {
                await this.QueuePacketAsync(new ContainerClosePacket { ContainerId = nextId });
                return;
            }

            contents = openedMenu.Concat(this.Inventory.Skip(9).Take(36)).ToList();
        }
        else
        {
            contents = container is CraftingTable { Type: InventoryType.Crafting } or EnchantmentTable
                or BeaconContainer or LecternContainer or CrafterContainer
                || Obsidian.Events.MainEventHandler.IsWorkstation(container)
                ? Obsidian.Events.MainEventHandler.WindowItems(this, container).ToList()
                : container.ToList();
        }

        await this.QueuePacketAsync(new ContainerSetContentPacket(nextId, contents)
        {
            CarriedItem = this.CarriedItem
        });

        if (container is EnchantmentTable)
            await RefreshEnchantingAsync();

        foreach (var property in Obsidian.Events.MainEventHandler.WorkstationProperties(this))
            await this.QueuePacketAsync(property);

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
            await this.QueuePacketAsync(new ContainerClosePacket { ContainerId = nextId });
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
            // Opening the open menu again ends its last opening, which its Closed hears of.
            if (container is MenuContainer && ReferenceEquals(this.OpenedContainer, container))
                this.OpenedContainer = null;

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
    ValueTask IPlayer.CloseContainerAsync(API.Plugins.IPluginContainer? unloading) =>
        this.CloseContainerAsync(unloading as PluginContainer);

    internal async ValueTask CloseContainerAsync(PluginContainer? unloading = null)
    {
        // Death owns mandatory window removal. A concurrent close must not return its transient inputs to inventory.
        if (!this.Alive)
            return;

        if (this.OpenedContainer is not BaseContainer container)
            return;

        var containerId = this.CurrentContainerId;
        if (container is MerchantContainer merchant)
            await merchant.CloseAsync(this, notify: false);

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

        this.RunMenusClosed();
        await this.QueuePacketAsync(new ContainerClosePacket { ContainerId = containerId });
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

    private async ValueTask TeleportWithinLevelAsync(ILevel level, VectorD pos, Angle? yaw, Angle? pitch)
    {
        FallDistance = 0;
        combatPosition = null;
        CancelWeaponUse();

        // The move, the client's view and the position packet change together under chunkUpdates, like a respawn's
        // and a level change's, so that the last position packet queued is always the player's position, whichever
        // travel came last.
        await this.chunkUpdates.WaitAsync();
        VectorD from;
        Angle fromYaw;
        Angle fromPitch;
        Entity[] riders;
        try
        {
            from = this.Position;
            fromYaw = this.Yaw;
            fromPitch = this.Pitch;
            riders = this.MoveWithinLevel(level, pos, yaw, pitch);
        }
        catch
        {
            this.chunkUpdates.Release();
            throw;
        }

        await using (DeferredInventoryWork.Begin(this.Logger))
        {
            try
            {
                await this.UpdateChunksUnderLockAsync(unloadAll: false);

                var tid = Globals.Random.Next(0, 999);
                await this.QueuePacketAsync(new PlayerPositionPacket
                {
                    Position = pos,
                    Delta = Motion,
                    Yaw = Yaw,
                    Pitch = Pitch,
                    TeleportId = tid
                });
                TeleportId = tid;
            }
            finally
            {
                this.chunkUpdates.Release();
            }
        }

        await EventDispatcher.ExecuteEventAsync(new PlayerTeleportEventArgs(this, this.Server, from, pos));
        await this.CarryPassengersAsync(riders, from, fromYaw, fromPitch);
    }

    // Moves the player within their level, with chunkUpdates held, after the travel that may have come first.
    // Returns the passengers it took off, to carry along.
    private Entity[] MoveWithinLevel(ILevel level, VectorD pos, Angle? yaw, Angle? pitch)
    {
        if (this.Level != level || !this.Alive)
            throw new InvalidOperationException("The player died or changed level before the teleport.");

        var riders = this.DetachPassengersForTravel();
        LastPosition = Position;
        if (Level is AbstractLevel resident && !resident.TryMoveEntity(this, Position, pos))
        {
            foreach (var rider in riders)
                rider.Ride(this, force: true);

            throw new InvalidOperationException("The destination region could not accept the player.");
        }

        Position = pos;
        this.Yaw = yaw ?? this.Yaw;
        this.Pitch = pitch ?? this.Pitch;
        BoundingBox = Dimension.CreateBBFromPosition(pos);

        return riders;
    }

    public override ValueTask TeleportAsync(IEntity to) =>
        this.TeleportAsync(to.Level, to.Position, to.Yaw, to.Pitch);

    public override ValueTask TeleportAsync(IWorld world) =>
        this.TeleportAsync(world, world.LevelData.SpawnPosition);

    public override ValueTask TeleportAsync(
        ILevel level, VectorD position, Angle? yaw = null, Angle? pitch = null) =>
        DeferredInventoryWork.RunOrDeferAsync(() => this.TeleportAfterCallbacksAsync(level, position, yaw, pitch));

    private async ValueTask TeleportAfterCallbacksAsync(
        ILevel level, VectorD position, Angle? yaw = null, Angle? pitch = null)
    {
        ValidateTravel(level, position, yaw, pitch);

        // A respawn under way (one whose packets a plugin's handler sees, say) finishes first. The respawn's own flow
        // never gets here meanwhile: travel it asks for is deferred past it.
        await this.WaitForRespawnAsync();
        if (!this.Alive)
            throw new InvalidOperationException("A dead player cannot travel.");

        await this.CancelPortalTravelAsync();
        if (this.Level != level)
        {
            await this.TransferDimensionAfterCallbacksAsync(level, position, yaw, pitch, null, false, false);
            return;
        }

        var source = this.Level;
        using var area = level is AbstractLevel resident
            ? await PortalArea.LoadAsync(resident, (Vector)position.Floor(), 0,
                () => this.Connected && this.Alive && this.Level == source)
            : null;
        if (level is AbstractLevel && area is null)
            throw new InvalidOperationException("The player moved while the destination was loading.");

        await this.TeleportWithinLevelAsync(level, position, yaw, pitch);
    }

    public virtual ValueTask SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature) =>
        throw new NotImplementedException();

    public ValueTask SendMessageAsync(ChatMessage message) =>
        this.QueuePacketAsync(new SystemChatPacket(message, false));

    public ValueTask SetActionBarTextAsync(ChatMessage message) =>
        this.QueuePacketAsync(new SystemChatPacket(message, true));

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

        await this.QueuePacketAsync(packet);
    }


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

    internal Task TransferDimensionAsync(
        ILevel destination, VectorD? position = null, Angle? yaw = null, Angle? pitch = null,
        VectorD? motion = null, bool portal = false, bool relativeMotion = false)
        => DeferredInventoryWork.RunOrDeferAsync(() => new ValueTask(this.TransferDimensionAfterCallbacksAsync(
            destination, position, yaw, pitch, motion, portal, relativeMotion))).AsTask();

    private async Task TransferDimensionAfterCallbacksAsync(
        ILevel destination, VectorD? position, Angle? yaw, Angle? pitch,
        VectorD? motion, bool portal, bool relativeMotion)
    {
        await this.CancelPortalTravelAsync();
        CancelEating();
        CancelWeaponUse();
        combatPosition = null;
        var origin = Level;
        var from = this.Position;
        var fromYaw = this.Yaw;
        var fromPitch = this.Pitch;
        Entity[] riders = [];
        var spawn = position ?? destination.LevelData.SpawnPosition;
        ValidateTravel(destination, spawn, yaw, pitch);
        using var area = destination is AbstractLevel target
            ? await PortalArea.LoadAsync(target, (Vector)spawn.Floor(), 0,
                () => this.Connected && this.Alive && this.IsTravelResident(origin))
            : null;
        if (destination is AbstractLevel && area is null)
            return;

        await this.CloseContainerAsync();

        while (true)
        {
            await this.WaitForDeathAsync();
            // Held from the level change until the destination's packets are queued, so that another travel's packets
            // can't come in between.
            await this.chunkUpdates.WaitAsync();
            await using var callbacks = DeferredInventoryWork.Begin(this.Logger);
            try
            {
                if (await this.RespawnUnderLockAsync(
                    DataKept.Attributes | DataKept.Metadata, levelChange: true, transfer: Commit))
                {
                    await this.QueueDestinationAsync(destination);
                    break;
                }
            }
            finally
            {
                this.preservePortalMotion = false;
                this.chunkUpdates.Release();
            }
        }

        await this.EventDispatcher.ExecuteEventAsync(new PlayerTeleportEventArgs(this, this.Server, from, spawn));
        await this.FireProgressAsync("changed_dimension", from: origin.DimensionName, to: destination.DimensionName);
        this.progressDimension = destination.DimensionName;
        await this.CarryPassengersAsync(riders, from, fromYaw, fromPitch);
        await SaveAsync();

        void Commit()
        {
            from = this.Position;
            fromYaw = this.Yaw;
            fromPitch = this.Pitch;
            riders = this.DetachPassengersForTravel();
            try
            {
                this.CommitTravel(origin, destination, spawn, yaw, pitch);
            }
            catch
            {
                foreach (var rider in riders)
                    rider.Ride(this, force: true);

                throw;
            }

            foreach (var observer in origin.GetPlayersInRange(from, float.MaxValue))
                observer.ForgetVisiblePlayer(this);

            this.Motion = motion ?? this.Motion;
            this.FallDistance = 0;
            this.foodPosition = null;
            this.preservePortalMotion = relativeMotion;
            if (portal)
                this.portalCooldown = PlayerPortalCooldown;
        }
    }

    // What a level change sends after the respawn packets, with chunkUpdates held.
    private async ValueTask QueueDestinationAsync(ILevel destination)
    {
        // Vanilla 1.21.11 sends the shared world's GlobalPos spawn, including while in a child dimension.
        var sharedSpawn = destination is IDimension child ? child.ParentWorld : destination;
        await this.QueuePacketAsync(SetTimePacket.Of(destination.LevelData));
        await this.QueuePacketAsync(new SetDefaultSpawnPositionPacket(new()
        {
            Dimension = sharedSpawn.DimensionName,
            Pos = (Vector)sharedSpawn.LevelData.SpawnPosition.Floor()
        }, 0, 0));
        var weather = destination.LevelData.Raining
            ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining;
        await this.QueuePacketAsync(new GameEventPacket(weather));
        foreach (var (id, effect) in ActivePotionEffects)
        {
            await this.QueuePacketAsync(new UpdateMobEffectPacket(EntityId, id, effect.CurrentDuration)
            {
                Amplifier = effect.EffectData.Amplifier,
                Flags = EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon
            });
        }

        await this.QueuePacketAsync(new GameEventPacket(
            ChangeGameStateReason.RainLevelChange, destination.LevelData.Raining ? 1 : 0));
        await this.QueuePacketAsync(new GameEventPacket(
            ChangeGameStateReason.ThunderLevelChange, destination.LevelData.Thundering ? 1 : 0));
        await this.QueuePacketAsync(new SetExperiencePacket(this.XpP, this.XpLevel, this.XpTotal));
        await this.QueuePacketAsync(new PlayerAbilitiesPacket { Abilities = this.Abilities });
    }

    /// <summary>
    /// Respawns the player, once a death under way finished, or sends them their level again if they're alive. A
    /// respawn asked for during another waits for it, then respawns the player again.
    /// </summary>
    public Task RespawnAsync(DataKept dataKept = DataKept.Metadata) =>
        DeferredInventoryWork.RunOrDeferAsync(() => new ValueTask(this.RespawnAfterDeathAsync(dataKept))).AsTask();

    private async Task RespawnAfterDeathAsync(DataKept dataKept)
    {
        while (true)
        {
            await this.WaitForDeathAsync();
            await this.chunkUpdates.WaitAsync();
            await using var callbacks = DeferredInventoryWork.Begin(this.Logger);
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
    /// Respawns the player, with <see cref="chunkUpdates"/> held, inside a transaction that defers plugins' callbacks
    /// (attachment changes, travel or deaths they ask for) until its release. Its packets are queued in order ahead of
    /// any chunk sent after it, and plugins' packet handlers see them in the client's pipeline, never in this flow.
    /// A dimension transfer commits through <paramref name="transfer"/> after the death check reserves this respawn.
    /// Returns false if death must finish first; the caller must release the lock before waiting and retrying.
    /// </summary>
    private async Task<bool> RespawnUnderLockAsync(
        DataKept dataKept, bool levelChange = false, Action? transfer = null)
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
        try
        {
            // Reserve the respawn before changing levels: a death that won the lock retries without committing,
            // and a death arriving after the reservation waits for the completed transfer.
            transfer?.Invoke();
            this.ResetClientView();
            this.ResetAttributeSync();
            var died = !Alive;
            if (died)
            {
                // Death respawns must create fresh client state rather than retain the dead player's metadata.
                dataKept = DataKept.None;
                // Death completed before taking chunkUpdates; never drain items again during respawn.
                API.Data.AttachmentStorage.For(this).Respawn(this);
                await this.StopSleepingAsync();
                var (respawnLevel, respawnPosition, respawnYaw) = await this.FindRespawnAsync();
                CancelEating();
                CancelWeaponUse();
                combatPosition = null;
                attackTicks = 0;
                LeaveVehicle();
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

                // Like vanilla's respawn, the player comes back where their bed or anchor is, or at the world spawn.
                if (respawnLevel != Level)
                {
                    Level.TryRemovePlayer(this);
                    Level = respawnLevel;
                    Level.TryAddPlayer(this);
                }

                Yaw = respawnYaw;
                Position = respawnPosition;
                LastPosition = Position;
                BoundingBox = Dimension.CreateBBFromPosition(Position);
                Level.TryAddEntity(this);
            }

            Log.ChangingLevel(this.Logger, this.Username, this.Level.Name);

            await this.QueuePacketAsync(new RespawnPacket
            {
                CommonPlayerSpawnInfo = this.CreateSpawnInfo(),
                DataKept = dataKept,
            });

            // Vanilla's level transfer keeps metadata and attributes, then sends the target difficulty.
            if (levelChange)
                await this.QueuePacketAsync(ChangeDifficultyPacket.Of(Level.LevelData));

            TeleportId = 0;

            await this.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks));
            await this.QueuePacketAsync(new PlayerPositionPacket
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
            await this.QueuePacketAsync(new SetHealthPacket(Health, FoodLevel, FoodSaturationLevel));
            await SendPlayerInfoAsync();

            // Vanilla's respawn sends the experience the new player starts with.
            if (died)
                await this.QueuePacketAsync(new SetExperiencePacket(this.XpP, this.XpLevel, this.XpTotal));

            await TrySpawnPlayerAsync(Position);
            await ((IPlayer)this).SynchronizeTrackedEntitiesAsync();
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

        // Vanilla syncs optional parrot variant ids, encoded as variant + 1 (zero clears the shoulder).
        writer.WriteEntityMetadataType(19, EntityMetadataType.OptionalUnsignedVarInt);
        writer.WriteVarInt(this.LeftShoulder is Parrot left ? left.Variant + 1 : 0);

        writer.WriteEntityMetadataType(20, EntityMetadataType.OptionalUnsignedVarInt);
        writer.WriteVarInt(this.RightShoulder is Parrot right ? right.Variant + 1 : 0);

        this.MetadataIndex = 21;
    }

    public async ValueTask SetGamemodeAsync(GameMode gamemode)
    {
        // Set first: the setter rejects unknown modes, and they must not reach clients.
        GameMode = gamemode;

        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new PlayerInfoUpdatePacket(CompilePlayerInfo(new UpdateGamemodeInfoAction(gamemode))));

        await this.QueuePacketAsync(new GameEventPacket(gamemode));
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

        await this.QueuePacketAsync(titlePacket);
        await this.QueuePacketAsync(titleTimesPacket);
    }

    public async ValueTask SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new SetSubtitleTextPacket
        {
            Text = subtitle
        };

        await this.QueuePacketAsync(titlePacket);

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

        await this.QueuePacketAsync(titlePacket);
        await this.QueuePacketAsync(titleTimesPacket);
    }

    public async ValueTask SendActionBarAsync(string text)
    {
        var actionBarPacket = new SetActionBarTextPacket
        {
            Text = text
        };

        await this.QueuePacketAsync(actionBarPacket);
    }

    //TODO 
    public virtual ValueTask SpawnParticleAsync(ParticleData data) => throw new NotImplementedException();

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

        // Only the node at the end of the path goes, with the permissions under it; its parents and siblings stay.
        var parent = PlayerPermissions;
        foreach (var permission in permissions[..^1])
        {
            parent = parent.Children.FirstOrDefault(x => x.Name.EqualsIgnoreCase(permission));
            if (parent is null)
                return false;
        }

        var node = parent.Children.FirstOrDefault(x => x.Name.EqualsIgnoreCase(permissions[^1]));
        if (node is null)
            return false;

        parent.Children.Remove(node);

        await this.SavePermsAsync();
        await this.Server.EventDispatcher.ExecuteEventAsync(new PermissionRevokedEventArgs(this, this.Server, permissionNode));

        return true;
    }

    /// <summary>
    /// Whether the player holds <paramref name="permissionNode"/>: every segment of its path was granted, or a
    /// wildcard on the way grants everything under it.
    /// </summary>
    public bool HasPermission(string permissionNode)
    {
        var parent = PlayerPermissions;
        if (parent.Children.Count == 0)
            return false;

        var permissions = permissionNode.ToLower().Trim().Split('.');

        foreach (var permission in permissions)
        {
            if (parent.Children.Any(x => x.Name == Permission.Wildcard))
                return true;

            parent = parent.Children.FirstOrDefault(x => x.Name.EqualsIgnoreCase(permission));
            if (parent is null)
                return false;
        }

        return true;
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
        this.PositionPassengers();
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
        this.PositionPassengers();
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

    /// <summary>
    /// Updates the chunks the player's client has around them (see <see cref="Player"/>). A player without a client has
    /// none to update.
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
    private protected virtual Task<bool> UpdateChunksUnderLockAsync(bool unloadAll, int distance = 0) =>
        Task.FromResult(true);

    // Shows the player the other players around them, once they've moved or respawned.
    private protected virtual ValueTask TrySpawnPlayerAsync(VectorD position) => default;

    // Forgets what the player's client was shown, which a respawn sends again.
    private protected virtual void ResetClientView() { }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Moving {Username} to {LevelName}")]
        public static partial void ChangingLevel(ILogger logger, string username, string levelName);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Username} was saved in {Dimension}; spawning them at the world spawn")]
        public static partial void OtherDimension(ILogger logger, string username, string dimension);

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Username}'s saved vehicle couldn't be restored; they join without it")]
        public static partial void VehicleNotRestored(ILogger logger, string username);
    }
}
