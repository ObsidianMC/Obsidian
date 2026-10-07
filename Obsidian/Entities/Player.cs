// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Scoreboard;
using Obsidian.WorldData;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:player")]
public sealed partial class Player : Avatar, IPlayer
{
    public byte CurrentContainerId { get; set; }

    public IClient Client { get; internal set; }

    public ILogger Logger => this.Client.Logger;

    internal HashSet<IPlayer> visiblePlayers = [];

    public bool IsDragging { get; set; }
    public List<short> DraggedSlots { get; set; } = [];
    public int TeleportId { get; set; }

    // <summary>
    /// Which chunks the player should have loaded around them.
    /// </summary>
    public ConcurrentHashSet<long> LoadedChunks { get; internal set; } = [];

    // The chunks in view that weren't generated when they were asked for, sent once they are, and the center and view
    // distance last sent to the client. Guarded by chunkUpdates, which keeps packet handlers and the level tick from
    // updating the client's chunks at the same time.
    private readonly HashSet<long> pendingChunks = [];
    private readonly SemaphoreSlim chunkUpdates = new(1, 1);
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

    public BaseContainer? OpenedContainer { get; set; }

    public List<SkinProperty> SkinProperties { get; set; } = [];

    public Vector? LastDeathLocation { get; set; }

    public ItemStack? CarriedItem { get; set; }

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

    public double HeadY { get; internal set; }

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
    public string PersistentDataBackupFile { get; }

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
        PersistentDataBackupFile = Path.Combine(ServerConstants.PersistentDataPath, $"{Uuid}.dat.old");

        Health = 20f;

        this.Client = client;
    }

    public ItemStack? GetHeldItem() => Inventory.GetItem(CurrentHeldItemSlot);
    public ItemStack? GetOffHandItem() => Inventory.GetItem(45);

    public async ValueTask DisplayScoreboardAsync(IScoreboard scoreboard, DisplaySlot slot)//TODO implement new features
    {
        var actualBoard = (Scoreboard)scoreboard;

        if (actualBoard.Objective is null)
            throw new InvalidOperationException("You must create an objective for the scoreboard before displaying it.");

        CurrentScoreboard = actualBoard;

        await Client.QueuePacketAsync(new SetObjectivePacket
        {
            ObjectiveName = actualBoard.name,
            Mode = ScoreboardMode.Create,
            Value = actualBoard.Objective.Value,
            Type = actualBoard.Objective.DisplayType
        });

        foreach (var (_, score) in actualBoard.scores)
        {
            await Client.QueuePacketAsync(new SetScorePacket
            {
                EntityName = score.DisplayText,
                ObjectiveName = actualBoard.name,
                Value = score.Value,
            });
        }

        await Client.QueuePacketAsync(new SetDisplayObjectivePacket
        {
            ObjectiveName = actualBoard.name,
            DisplaySlot = slot
        });
    }

    public async ValueTask OpenInventoryAsync(BaseContainer container)
    {
        OpenedContainer = container;

        var nextId = GetNextContainerId();

        await Client.QueuePacketAsync(new OpenScreenPacket(container, nextId));

        if (container.HasItems())
            await Client.QueuePacketAsync(new ContainerSetContentPacket(nextId, container.ToList()));
    }

    public async override ValueTask TeleportAsync(VectorD pos)
    {
        Vehicle?.Dismount(this);
        LastPosition = Position;
        Position = pos;
        await UpdateChunksAsync();

        var tid = Globals.Random.Next(0, 999);

        await EventDispatcher.ExecuteEventAsync(
            new PlayerTeleportEventArgs
            (
                this,
                this.Server,
                Position,
                pos
            ));

        await Client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = pos,
            TeleportId = tid
        });
        TeleportId = tid;
    }

    public async override ValueTask TeleportAsync(IEntity to)
    {
        LastPosition = Position;
        Position = to.Position;

        await UpdateChunksAsync();

        TeleportId = Globals.Random.Next(0, 999);

        await Client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = to.Position,
            TeleportId = TeleportId
        });
    }

    public async override ValueTask TeleportAsync(IWorld world)
    {
        if (world is not World w)
        {
            await base.TeleportAsync(world);
            return;
        }

        // save current world/persistent data 
        await SaveAsync();

        Level.TryRemovePlayer(this);
        w.TryAddPlayer(this);

        Level = w;

        // resync player data
        await LoadAsync(false);

        // reload world stuff and send rest of the info
        await UpdateChunksAsync(true);

        await SendPlayerInfoAsync();
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

    internal async Task TransferDimensionAsync(ILevel destination)
    {
        CancelEating();
        Vehicle?.Dismount(this);
        var origin = Level;
        var spawn = destination.LevelData.SpawnPosition;
        await destination.GetChunkAsync((Vector)spawn.Floor());
        await origin.DestroyEntityAsync(this);
        origin.TryRemovePlayer(this);
        foreach (var observer in origin.GetPlayersInRange(Position, float.MaxValue).OfType<Player>())
            observer.visiblePlayers.Remove(this);
        Level = destination;
        Position = spawn;
        LastPosition = spawn;
        BoundingBox = Dimension.CreateBBFromPosition(spawn);
        HeadY = spawn.Y + 1.62;
        Motion = VectorD.Zero;
        FallDistance = 0;
        foodPosition = null;
        destination.TryAddPlayer(this);
        destination.TryAddEntity(this);
        await RespawnAsync();
        await Client.QueuePacketAsync(new SetDefaultSpawnPositionPacket(new()
        { Dimension = destination.DimensionName, Pos = (Vector)spawn.Floor() }, 0, 0));
        await Client.QueuePacketAsync(new SetTimePacket(destination.LevelData.Time, destination.LevelData.DayTime, true));
        await Client.QueuePacketAsync(new GameEventPacket(destination.LevelData.Raining ? ChangeGameStateReason.BeginRaining : ChangeGameStateReason.EndRaining));
        foreach (var (id, effect) in ActivePotionEffects)
            await Client.QueuePacketAsync(new UpdateMobEffectPacket(EntityId, id, effect.CurrentDuration)
            { Amplifier = effect.EffectData.Amplifier, Flags = EntityEffectFlags.ShowParticles | EntityEffectFlags.ShowIcon });
        await SaveAsync();
    }

    public async Task RespawnAsync(DataKept dataKept = DataKept.Metadata)
    {
        if (Respawning) return;
        Respawning = true;
        try
        {
            if (!Alive)
            {
                CancelEating();
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
                Health = 20f;
                await Level.DestroyEntityAsync(this);
                Position = Level.LevelData.SpawnPosition;
                LastPosition = Position;
                BoundingBox = Dimension.CreateBBFromPosition(Position);
                HeadY = Position.Y + 1.62;
                Level.TryAddEntity(this);
            }

            CodecRegistry.TryGetDimension(Level.DimensionName, out var codec);
            Debug.Assert(codec is not null); // TODO Handle missing codec

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
                },
                DataKept = dataKept,
            });

            visiblePlayers.Clear();

            TeleportId = 0;

            await Client.QueuePacketAsync(new GameEventPacket(ChangeGameStateReason.StartWaitingForLevelChunks));
            await Client.QueuePacketAsync(new PlayerPositionPacket
            {
                Position = Position,
                Yaw = Yaw,
                Pitch = Pitch,
                TeleportId = 0
            });

            pendingRespawnChunks = !await UpdateChunksAsync(true);
            respawnChunkRetryTicks = 20;
            Level.TryAddEntity(this);
            await Client.QueuePacketAsync(new SetHealthPacket(Health, FoodLevel, FoodSaturationLevel));
            await SendPlayerInfoAsync();
            await TrySpawnPlayerAsync(Position);
            await SynchronizeTrackedEntitiesAsync();
        }
        finally
        {
            Respawning = false;
        }

    }

    //TODO make IDamageSource 
    public async override ValueTask KillAsync(IEntity source, ChatMessage deathMessage)
    {
        Health = 0;
        CancelEating();
        Vehicle?.Dismount(this);
        await Client.QueuePacketAsync(new SetHealthPacket(0, FoodLevel, FoodSaturationLevel));
        await Client.QueuePacketAsync(new PlayerCombatKillPacket { PlayerID = EntityId, Message = deathMessage });
        PacketBroadcaster.QueuePacketToLevel(Level, new EntityEventPacket { EntityId = EntityId, Event = 3 }, EntityId);
        foreach (var observer in Level.GetPlayersInRange(Position, float.MaxValue).OfType<Player>())
            observer.visiblePlayers.Remove(this);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Float);
        writer.WriteSingle(Absorption);

        this.WriteEntityMetadataType(writer, EntityMetadataType.VarInt);
        writer.WriteVarInt(XpTotal);

        //TODO fix possibly an extension method?
        if (this.LeftShoulder is not null)
        {
            this.WriteEntityMetadataType(writer, EntityMetadataType.OptionalLivingEntityReference);
            writer.WriteNbtCompound([]);
        }

        if (this.RightShoulder is not null)
        {
            if (this.LeftShoulder is null)
                this.MetadataIndex++;

            this.WriteEntityMetadataType(writer, EntityMetadataType.OptionalLivingEntityReference);
            writer.WriteNbtCompound([]);
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
        this.PacketBroadcaster.QueuePacketToLevel(this.Level, new PlayerInfoUpdatePacket(CompilePlayerInfo(new UpdateDisplayNameInfoAction(newDisplayName))));

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
        if (Vehicle != null)
            position = Position;
        var oldPosition = Position;
        var oldMovementFlags = MovementFlags;
        await base.UpdateAsync(position, movementFlags);
        if (Level is AbstractLevel events) await events.EmitMovementGameEventsAsync(this, oldPosition, oldMovementFlags);

        HeadY = position.Y + 1.62f;

        await TrySpawnPlayerAsync(position);

        await PickupNearbyItemsAsync();
    }

    public async override ValueTask UpdateAsync(VectorD position, Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        if (!Alive || Respawning) return;
        if (Vehicle != null)
            position = Position;
        var oldPosition = Position;
        var oldMovementFlags = MovementFlags;
        await base.UpdateAsync(position, yaw, pitch, movementFlags);
        if (Level is AbstractLevel events) await events.EmitMovementGameEventsAsync(this, oldPosition, oldMovementFlags);

        HeadY = position.Y + 1.62f;

        await TrySpawnPlayerAsync(position);

        await PickupNearbyItemsAsync();
    }

    public async override ValueTask UpdateAsync(Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        if (!Alive || Respawning) return;
        await base.UpdateAsync(yaw, pitch, movementFlags);

        await PickupNearbyItemsAsync();
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
                this.chunkCacheCenter = null;
            }

            var (centerX, centerZ) = Position.ToChunkCoord();

            // The client drops chunks outside the range around its center, so the center goes first.
            if (this.chunkCacheCenter != (centerX, centerZ))
            {
                this.chunkCacheCenter = (centerX, centerZ);
                await Client.QueuePacketAsync(new SetChunkCacheCenterPacket(centerX, centerZ));
            }

            // Like vanilla, at least 2.
            this.chunkViewDistance = Math.Max(2, distance < 1 ? ClientInformation.ViewDistance : distance);

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
        finally
        {
            this.chunkUpdates.Release();
        }
    }

    /// <summary>
    /// Sends the chunks in view that weren't generated when they were last asked for and are now. The level calls it every
    /// tick; it skips the tick while the chunks are being updated.
    /// </summary>
    internal async Task SendPendingChunksAsync()
    {
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

    // Sends the pending chunks that are generated, nearest to the center first, and asks for the others again (generation
    // only queues them once). Returns whether none are left. Called under chunkUpdates.
    private async Task<bool> SendReadyChunksAsync()
    {
        if (this.pendingChunks.Count == 0 || this.chunkCacheCenter is not var (centerX, centerZ))
            return this.pendingChunks.Count == 0;

        var chunks = this.pendingChunks.ToArray();
        var distances = new int[chunks.Length];
        for (var i = 0; i < chunks.Length; i++)
        {
            NumericsHelper.LongToInts(chunks[i], out var x, out var z);
            distances[i] = (x - centerX) * (x - centerX) + (z - centerZ) * (z - centerZ);
        }

        Array.Sort(distances, chunks);

        foreach (var value in chunks)
        {
            NumericsHelper.LongToInts(value, out var x, out var z);
            if (await Level.GetChunkAsync(x, z) is not IChunk chunk || !chunk.IsGenerated)
                continue;

            await Client.QueuePacketAsync(new LevelChunkWithLightPacket(chunk));
            LoadedChunks.Add(value);
            this.pendingChunks.Remove(value);
        }

        return this.pendingChunks.Count == 0;
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

        [LoggerMessage(Level = LogLevel.Warning, Message = "{Username} has invalid saved data; spawning them at the world spawn")]
        public static partial void InvalidSavedData(ILogger logger, Exception exception, string username);
    }
}
