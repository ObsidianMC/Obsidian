// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.API.Utilities;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Scoreboard;
using Obsidian.Registries;
using Obsidian.WorldData;
using System.Buffers;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Net;

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:player")]
public sealed partial class Player : Living, IPlayer
{
    private byte containerId = 0;

    internal readonly Client client;

    /// <summary>
    /// Used to log actions caused by the client.
    /// </summary>
    protected ILogger Logger { get; private set; }

    internal HashSet<IPlayer> visiblePlayers = [];

    //TODO: better name??
    internal short inventorySlot = 36;

    internal bool isDragging;

    internal int TeleportId { get; set; }

    //TODO 
    public bool IsOperator { get; }

    public string Username { get; }

    public ClientInformation ClientInformation { get; internal set; }

    /// <summary>
    /// The players inventory.
    /// </summary>
    public Container Inventory { get; }
    public Container EnderInventory { get; }

    public BaseContainer? OpenedContainer { get; set; }

    public List<SkinProperty> SkinProperties { get; set; } = [];

    public Vector? LastDeathLocation { get; set; }

    public ItemStack? LastClickedItem { get; internal set; }

    public IBlock? LastClickedBlock { get; internal set; }

    public Gamemode Gamemode
    {
        get => gamemode;
        set
        {
            gamemode = value;

            Abilities = Gamemode switch
            {
                Gamemode.Creative => PlayerAbility.CreativeMode | PlayerAbility.AllowFlying | PlayerAbility.Invulnerable,
                Gamemode.Spectator => PlayerAbility.AllowFlying | PlayerAbility.Invulnerable,
                Gamemode.Survival or Gamemode.Adventure or Gamemode.Hardcore => PlayerAbility.None,
                _ => throw new ArgumentOutOfRangeException(nameof(Gamemode), Gamemode, "Unknown gamemode.")
            };
        }
    }

    public PlayerAbility Abilities { get; internal set; }

    public IScoreboard? CurrentScoreboard { get; set; }

    public bool Sleeping { get; set; }
    public bool InHorseInventory { get; set; }
    public bool Respawning { get; internal set; }

    public short AttackTime { get; set; }
    public short DeathTime { get; set; }
    public short HurtTime { get; set; }
    public short SleepTimer { get; set; }

    public short CurrentSlot
    {
        get => (short)(inventorySlot - 36);
        internal set
        {
            if (value is < 0 or > 8)
                throw new IndexOutOfRangeException("Value must be >= 0 or <= 8");

            inventorySlot = (short)(value + 36);
        }
    }

    public int Ping => client.ping;
    public int FoodLevel { get; set; }
    public int FoodTickTimer { get; set; }
    public int XpLevel { get; set; }
    public int XpTotal { get; set; }
    public float XpP { get; set; } = 0;

    public double HeadY { get; private set; }

    public float AdditionalHearts { get; set; } = 0;
    public float FallDistance { get; set; }
    public float FoodExhaustionLevel { get; set; }
    public float FoodSaturationLevel { get; set; }

    public Entity? LeftShoulder { get; set; }
    public Entity? RightShoulder { get; set; }

    // Properties set by Obsidian (unofficial)
    // Not sure whether these should be saved to the NBT file.
    // These could be saved under nbt tags prefixed with "obsidian_"
    // As minecraft might just ignore them.
    public Permission PlayerPermissions { get; private set; } = new Permission("root");

    public string PersistentDataFile { get; }
    public string PersistentDataBackupFile { get; }

    public IPAddress? ClientIP => (client.RemoteEndPoint as IPEndPoint)?.Address;

    private Gamemode gamemode;

    [SetsRequiredMembers]
    internal Player(Guid uuid, string username, Client client, World world)
    {
        Uuid = uuid;
        Username = username;
        this.client = client;
        EntityId = client.id;

        Inventory = new Container(9 * 5 + 1, InventoryType.Generic)
        {
            Owner = uuid,
            IsPlayerInventory = true
        };
        EnderInventory = new Container
        {
            Title = "Ender Chest"
        };

        base.world = world;
        Type = EntityType.Player;

        PersistentDataFile = Path.Combine(Server.PersistentDataPath, $"{Uuid}.dat");
        PersistentDataBackupFile = Path.Combine(Server.PersistentDataPath, $"{Uuid}.dat.old");

        Health = 20f;
        this.PacketBroadcaster = world.PacketBroadcaster;
    }

    public ItemStack? GetHeldItem() => Inventory.GetItem(inventorySlot);
    public ItemStack? GetOffHandItem() => Inventory.GetItem(45);

    public async Task LoadPermsAsync()
    {
        // Load a JSON file that contains all permissions
        var file = new FileInfo(Path.Combine(Server.PermissionPath, $"{Uuid}.json"));

        if (file.Exists)
        {
            await using var fs = file.OpenRead();
            if (await fs.FromJsonAsync<Permission>() is Permission permission)
                PlayerPermissions = permission;
        }
    }

    public async Task SavePermsAsync()
    {
        // Save permissions to JSON file
        var file = new FileInfo(Path.Combine(Server.PermissionPath, $"{Uuid}.json"));

        await using var fs = file.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);

        await PlayerPermissions.ToJsonAsync(fs);
    }

    public async Task DisplayScoreboardAsync(IScoreboard scoreboard, DisplaySlot position)//TODO implement new features
    {
        var actualBoard = (Scoreboard)scoreboard;

        if (actualBoard.Objective is null)
            throw new InvalidOperationException("You must create an objective for the scoreboard before displaying it.");

        CurrentScoreboard = actualBoard;

        await client.QueuePacketAsync(new SetObjectivePacket
        {
            ObjectiveName = actualBoard.name,
            Mode = ScoreboardMode.Create,
            Value = actualBoard.Objective.Value,
            Type = actualBoard.Objective.DisplayType
        });

        foreach (var (_, score) in actualBoard.scores)
        {
            await client.QueuePacketAsync(new SetScorePacket
            {
                EntityName = score.DisplayText,
                ObjectiveName = actualBoard.name,
                Value = score.Value,
            });
        }

        await client.QueuePacketAsync(new SetDisplayObjectivePacket
        {
            ObjectiveName = actualBoard.name,
            DisplaySlot = position
        });
    }

    public async Task OpenInventoryAsync(BaseContainer container)
    {
        OpenedContainer = container;

        var nextId = GetNextContainerId();

        await client.QueuePacketAsync(new OpenScreenPacket(container, nextId));

        if (container.HasItems())
            await client.QueuePacketAsync(new ContainerSetContentPacket(nextId, container.ToList()));
    }

    public async override ValueTask TeleportAsync(VectorF pos)
    {
        LastPosition = Position;
        Position = pos;
        await UpdateChunksAsync(true, 2);

        var tid = Globals.Random.Next(0, 999);

        await client.server.EventDispatcher.ExecuteEventAsync(
            new PlayerTeleportEventArgs
            (
                this,
                this.client.server,
                Position,
                pos
            ));

        await client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = pos,
            Flags = PositionFlags.None,
            TeleportId = tid
        });
        TeleportId = tid;
    }

    public async override ValueTask TeleportAsync(IEntity to)
    {
        LastPosition = Position;
        Position = to.Position;

        await UpdateChunksAsync(true, 2);

        TeleportId = Globals.Random.Next(0, 999);

        await client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = to.Position,
            Flags = PositionFlags.None,
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

        base.world.TryRemovePlayer(this);
        w.TryAddPlayer(this);

        base.world = w;

        // resync player data
        await LoadAsync(false);

        // reload world stuff and send rest of the info
        await UpdateChunksAsync(true, 2);

        await client.SendInfoAsync();

        var (chunkX, chunkZ) = Position.ToChunkCoord();
        await client.QueuePacketAsync(new SetChunkCacheCenterPacket(chunkX, chunkZ));
    }

    public Task SendMessageAsync(ChatMessage message, Guid sender, SecureMessageSignature messageSignature) =>
        throw new NotImplementedException();

    public Task SendMessageAsync(ChatMessage message) =>
        client.QueuePacketAsync(new SystemChatPacket(message, false));

    public Task SetActionBarTextAsync(ChatMessage message) =>
        client.QueuePacketAsync(new SystemChatPacket(message, true));

    public async Task SendSoundAsync(ISoundEffect soundEffect)
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

        await client.QueuePacketAsync(packet);
    }

    public Task KickAsync(string reason) => client.DisconnectAsync(ChatMessage.Simple(reason));
    public Task KickAsync(ChatMessage reason) => client.DisconnectAsync(reason);

    public async Task RespawnAsync(DataKept dataKept = DataKept.Metadata)
    {
        if (!Alive)
        {
            // if unalive, reset health and set location to world spawn
            Health = 20f;
            Position = world.LevelData.SpawnPosition;
        }

        CodecRegistry.TryGetDimension(world.DimensionName, out var codec);
        Debug.Assert(codec is not null); // TODO Handle missing codec

        Logger.LogDebug("Loading into world: {}", world.Name);

        await client.QueuePacketAsync(new RespawnPacket
        {
            CommonPlayerSpawnInfo = new()
            {
                DimensionType = codec.Id,
                DimensionName = world.DimensionName,
                Gamemode = Gamemode,
                PreviousGamemode = Gamemode,
                HashedSeed = 0,
                Flat = false,
                Debug = false,
            },
            DataKept = dataKept,
        });

        visiblePlayers.Clear();

        Respawning = true;
        TeleportId = 0;

        await UpdateChunksAsync(true, 2);

        await client.QueuePacketAsync(new PlayerPositionPacket
        {
            Position = Position,
            Yaw = 0,
            Pitch = 0,
            Flags = PositionFlags.None,
            TeleportId = 0
        });

        Respawning = false;
    }

    //TODO make IDamageSource 
    public async override ValueTask KillAsync(IEntity source, ChatMessage deathMessage)
    {
        //await this.client.QueuePacketAsync(new PlayerDied
        //{
        //    PlayerId = this.EntityId,
        //    EntityId = source != null ? source.EntityId : -1,
        //    Message = deathMessage as ChatMessage
        //});
        // TODO implement new death packets

        await client.QueuePacketAsync(new GameEventPacket(RespawnReason.EnableRespawnScreen));
        await RemoveAsync();

        if (source is Player attacker)
            attacker.visiblePlayers.Remove(this);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(15, EntityMetadataType.Float);
        writer.WriteFloat(AdditionalHearts);

        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt(XpTotal);

        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte((byte)ClientInformation.DisplayedSkinParts);

        writer.WriteEntityMetadataType(18, EntityMetadataType.Byte);
        writer.WriteByte((byte)ClientInformation.MainHand);

        if (LeftShoulder is not null)
        {
            writer.WriteEntityMetadataType(19, EntityMetadataType.Nbt);
            ((MinecraftStream)writer).WriteNbtCompound(new NbtCompound());
        }

        if (RightShoulder is not null)
        {
            writer.WriteEntityMetadataType(20, EntityMetadataType.Nbt);
            ((MinecraftStream)writer).WriteNbtCompound(new NbtCompound());
        }
    }

    public async Task SetGamemodeAsync(Gamemode gamemode)
    {
        this.PacketBroadcaster.QueuePacketToWorld(this.World, new PlayerInfoUpdatePacket(CompilePlayerInfo(new UpdateGamemodeInfoAction(gamemode))));

        await client.QueuePacketAsync(new GameEventPacket(gamemode));

        Gamemode = gamemode;
    }

    public Task UpdateDisplayNameAsync(string newDisplayName)
    {
        this.PacketBroadcaster.QueuePacketToWorld(this.World, new PlayerInfoUpdatePacket(CompilePlayerInfo(new UpdateDisplayNameInfoAction(newDisplayName))));

        CustomName = newDisplayName;

        return Task.CompletedTask;
    }

    public async Task SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut)
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

        await client.QueuePacketAsync(titlePacket);
        await client.QueuePacketAsync(titleTimesPacket);
    }

    public async Task SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new SetSubtitleTextPacket
        {
            Text = subtitle
        };

        await client.QueuePacketAsync(titlePacket);

        await SendTitleAsync(title, fadeIn, stay, fadeOut);
    }

    public async Task SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
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

        await client.QueuePacketAsync(titlePacket);
        await client.QueuePacketAsync(titleTimesPacket);
    }

    public async Task SendActionBarAsync(string text)
    {
        var actionBarPacket = new SetActionBarTextPacket
        {
            Text = text
        };

        await client.QueuePacketAsync(actionBarPacket);
    }

    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float extra = 0) =>
        SpawnParticleAsync(particle, new VectorF(x, y, z), count, extra);

    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float offsetX,
        float offsetY, float offsetZ, float extra = 0) =>
        SpawnParticleAsync(particle, new VectorF(x, y, z), count, offsetX, offsetY, offsetZ, extra);


    public async Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float extra = 0) =>
        await client.QueuePacketAsync(new LevelParticlesPacket
        {
            Position = pos,
            ParticleCount = count,
            MaxSpeed = extra
        });

    public async Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float offsetX, float offsetY,
        float offsetZ, float extra = 0) => await client.QueuePacketAsync(
        new LevelParticlesPacket
        {
            Position = pos,
            ParticleCount = count,
            Offset = new VectorF(offsetX, offsetY, offsetZ),
            MaxSpeed = extra
        });

    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, ParticleData data,
        float extra = 0) =>
        SpawnParticleAsync(particle, new VectorF(x, y, z), count, extra);

    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float offsetX, float offsetY, float offsetZ, ParticleData data, float extra = 0) =>
        SpawnParticleAsync(particle, new VectorF(x, y, z), count, offsetX, offsetY, offsetZ, extra);

    public async Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, ParticleData data,
        float extra = 0) =>
        await client.QueuePacketAsync(new LevelParticlesPacket
        {
            Position = pos,
            ParticleCount = count,
            Data = data,
            MaxSpeed = extra
        });

    public async Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float offsetX, float offsetY,
        float offsetZ, ParticleData data, float extra = 0) => await client.QueuePacketAsync(
        new LevelParticlesPacket
        {
            Position = pos,
            ParticleCount = count,
            Data = data,
            Offset = new VectorF(offsetX, offsetY, offsetZ),
            MaxSpeed = extra
        });

    public async Task SaveAsync()
    {
        var playerDataFile = new FileInfo(GetPlayerDataPath());
        var persistentDataFile = new FileInfo(PersistentDataFile);

        if (playerDataFile.Exists)
        {
            playerDataFile.CopyTo(GetPlayerDataPath(true), true);
            playerDataFile.Delete();
        }

        if (persistentDataFile.Exists)
        {
            persistentDataFile.CopyTo(PersistentDataBackupFile, true);
            persistentDataFile.Delete();
        }

        await using var persistentDataStream = persistentDataFile.Create();
        await using var persistentDataWriter = new NbtWriter(persistentDataStream, NbtCompression.GZip, "");

        persistentDataWriter.WriteString("worldName", world.ParentWorldName ?? world.Name);
        //TODO make sure to save inventory in the right location if has using global data set to true

        persistentDataWriter.EndCompound();
        await persistentDataWriter.TryFinishAsync();

        await using var playerFileStream = playerDataFile.Create();
        await using var writer = new NbtWriter(playerFileStream, NbtCompression.GZip, "");

        writer.WriteInt("DataVersion", 3337);
        writer.WriteInt("playerGameType", (int)Gamemode);
        writer.WriteInt("previousPlayerGameType", (int)Gamemode);
        writer.WriteInt("Score", 0);
        writer.WriteInt("SelectedItemSlot", inventorySlot);
        writer.WriteInt("foodLevel", FoodLevel);
        writer.WriteInt("foodTickTimer", FoodTickTimer);
        writer.WriteInt("XpLevel", XpLevel);
        writer.WriteInt("XpTotal", XpTotal);

        writer.WriteFloat("Health", Health);

        writer.WriteFloat("foodExhaustionLevel", FoodExhaustionLevel);
        writer.WriteFloat("foodSaturationLevel", FoodSaturationLevel);

        writer.WriteString("Dimension", world.DimensionName);

        writer.WriteListStart("Pos", NbtTagType.Double, 3);

        writer.WriteDouble(Position.X);
        writer.WriteDouble(Position.Y);
        writer.WriteDouble(Position.Z);

        writer.EndList();

        writer.WriteListStart("Rotation", NbtTagType.Float, 2);

        writer.WriteFloat(Yaw);
        writer.WriteFloat(Pitch);

        writer.EndList();

        WriteItems(writer);
        WriteItems(writer, false);

        writer.EndCompound();

        await writer.TryFinishAsync();
    }

    public async Task LoadAsync(bool loadFromPersistentWorld = true)
    {
        // Read persistent data first
        var persistentDataFile = new FileInfo(PersistentDataFile);

        if (persistentDataFile.Exists)
        {
            await using var persistentDataStream = persistentDataFile.OpenRead();

            var persistentDataReader = new NbtReader(persistentDataStream, NbtCompression.GZip);

            //TODO use inventory if has using global data set to true
            if (persistentDataReader.ReadNextTag() is NbtCompound persistentDataCompound)
            {
                var worldName = persistentDataCompound.GetString("worldName")!;

                Logger?.LogInformation("persistent world: {worldName}", worldName);

                if (loadFromPersistentWorld && this.world.WorldManager.TryGetWorld<World>(worldName, out var world))
                {
                    base.world = world;
                    Logger?.LogInformation("Loading from persistent world: {worldName}", worldName);
                }
            }
        }

        // Then read player data
        var playerDataFile = new FileInfo(GetPlayerDataPath());

        await LoadPermsAsync();

        if (!playerDataFile.Exists)
        {
            Position = world.LevelData.SpawnPosition;
            return;
        }

        await using var playerFileStream = playerDataFile.OpenRead();

        var reader = new NbtReader(playerFileStream, NbtCompression.GZip);

        var compound = reader.ReadNextTag() as NbtCompound;
        Debug.Assert(compound is not null); // TODO Handle invalid NBT

        MovementFlags = compound.GetBool("OnGround") ? MovementFlags.OnGround : default;
        Sleeping = compound.GetBool("Sleeping");
        Air = compound.GetShort("Air");
        AttackTime = compound.GetShort("AttackTime");
        DeathTime = compound.GetShort("DeathTime");
        Health = compound.GetFloat("Health");
        HurtTime = compound.GetShort("HurtTime");
        SleepTimer = compound.GetShort("SleepTimer");
        FoodLevel = compound.GetInt("foodLevel");
        FoodTickTimer = compound.GetInt("foodTickTimer");
        Gamemode = (Gamemode)compound.GetInt("playerGameType");
        XpLevel = compound.GetInt("XpLevel");
        XpTotal = compound.GetInt("XpTotal");
        FallDistance = compound.GetFloat("FallDistance");
        FoodExhaustionLevel = compound.GetFloat("foodExhaustionLevel");
        FoodSaturationLevel = compound.GetFloat("foodSaturationLevel");
        XpP = compound.GetInt("XpP");

        if (!Alive)
            Health = 20f;//Player should never load data that has health at 0 

        var dimensionName = compound.GetString("Dimension");
        if (!string.IsNullOrWhiteSpace(dimensionName) && CodecRegistry.TryGetDimension(dimensionName, out var codec))
        {
            //TODO load into dimension ^ ^
        }

        compound.TryGetTag("Pos", out var posTag);
        Position = (posTag as NbtList) switch
        {
            [NbtTag<double> a, NbtTag<double> b, NbtTag<double> c, ..] => new VectorF((float)a.Value, (float)b.Value, (float)c.Value),
            _ => world.LevelData.SpawnPosition
        };

        if (compound.TryGetTag("Rotation", out var rotationTag))
        {
            if (rotationTag is NbtList and [NbtTag<float> yaw, NbtTag<float> pitch, ..])
            {
                Yaw = yaw.Value;
                Pitch = pitch.Value;
            }
        }

        if (compound.TryGetTag("Inventory", out var rawTag) && rawTag is NbtList inventory)
        {
            foreach (var rawItemTag in inventory)
            {
                if (rawItemTag.Type == NbtTagType.End)
                    break;

                if (rawItemTag is not NbtCompound itemCompound)
                    continue;

                var slot = itemCompound.GetByte("Slot");

                var itemMetaBuilder = new ItemMetaBuilder()
                    .WithDurability(itemCompound.GetInt("Damage"))
                    .IsUnbreakable(itemCompound.GetBool("Unbreakable"));

                var item = ItemsRegistry.GetSingleItem(itemCompound.GetString("id"), itemMetaBuilder.Build());
                item.Count = itemCompound.GetByte("Count");
                item.Slot = slot;

                Inventory.SetItem(slot, item);
            }
        }
    }

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
            await this.client.server.EventDispatcher.ExecuteEventAsync(new PermissionGrantedEventArgs(this, this.client.server, permissionNode));

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
                await this.client.server.EventDispatcher.ExecuteEventAsync(new PermissionRevokedEventArgs(this, this.client.server, permissionNode));

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

    public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.Count(x => HasPermission(x)) == permissions.Count();

    public byte GetNextContainerId()
    {
        containerId = (byte)(containerId % 255 + 1);

        return containerId;
    }

    public override string ToString() => Username;

    internal async override ValueTask UpdateAsync(VectorF position, MovementFlags movementFlags)
    {
        await base.UpdateAsync(position, movementFlags);

        HeadY = position.Y + 1.62f;

        await TrySpawnPlayerAsync(position);

        await PickupNearbyItemsAsync();
    }

    internal async override ValueTask UpdateAsync(VectorF position, Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        await base.UpdateAsync(position, yaw, pitch, movementFlags);

        HeadY = position.Y + 1.62f;

        await TrySpawnPlayerAsync(position);

        await PickupNearbyItemsAsync();
    }

    internal async override ValueTask UpdateAsync(Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        await base.UpdateAsync(yaw, pitch, movementFlags);

        await PickupNearbyItemsAsync();
    }

    private async ValueTask TrySpawnPlayerAsync(VectorF position)
    {
        //TODO PROPER DISTANCE CALCULATION
        var entityBroadcastDistance = this.world.Configuration.EntityBroadcastRangePercentage;

        foreach (var player in world.GetPlayersInRange(position, entityBroadcastDistance))
        {
            if (player == this)
                continue;

            if (player.Alive && !visiblePlayers.Contains(player))
            {
                visiblePlayers.Add(player);

                player.SpawnEntity();
            }
        }

        if (visiblePlayers.Count == 0)
            return;

        var removed = ArrayPool<int>.Shared.Rent(visiblePlayers.Count);

        var index = 0;
        visiblePlayers.RemoveWhere(visiblePlayer =>
        {
            if (!visiblePlayer.IsInRange(this, entityBroadcastDistance))
            {
                removed[index++] = visiblePlayer.EntityId;
                return true;
            }
            return false;
        });

        if (index > 0)
            await client.QueuePacketAsync(new RemoveEntitiesPacket(removed.ToArray()));

        ArrayPool<int>.Shared.Return(removed);
    }

    private async Task PickupNearbyItemsAsync(float distance = 1.5f)
    {
        foreach (var entity in world.GetNonPlayerEntitiesInRange(Position, distance))
        {
            if (entity is not ItemEntity item)
                continue;

            if (!item.CanPickup)
                continue;

            this.PacketBroadcaster.QueuePacketToWorld(this.World, new TakeItemEntityPacket
            {
                CollectedEntityId = item.EntityId,
                CollectorEntityId = EntityId,
                PickupItemCount = item.Count
            });

            var slot = Inventory.AddItem(new ItemStack(item.Material, item.Count, item.ItemMeta));

            client.SendPacket(new ContainerSetSlotPacket
            {
                Slot = (short)slot,
                ContainerId = 0,
                SlotData = Inventory.GetItem(slot)!,
                StateId = Inventory.StateId++
            });

            await item.RemoveAsync();
        }
    }

    /// <summary>
    /// Updates client chunks. Only send <paramref name="distance"/> when sending initial chunks.
    /// </summary>
    /// <param name="unloadAll"></param>
    /// <param name="distance"></param>
    /// <returns>Whether all chunks have been sent.</returns>
    internal async Task<bool> UpdateChunksAsync(bool unloadAll = false, int distance = 0)
    {
        bool sentAll = true;
        if (unloadAll)
        {
            if (!Respawning)
            {
                foreach (var (X, Z) in client.LoadedChunks)
                    await client.UnloadChunkAsync(X, Z);
            }

            client.LoadedChunks.Clear();
        }

        List<(int X, int Z)> clientNeededChunks = [];
        List<(int X, int Z)> clientUnneededChunks = new(client.LoadedChunks);

        (int playerChunkX, int playerChunkZ) = Position.ToChunkCoord();

        int dist = distance < 1 ? ClientInformation.ViewDistance : distance;
        for (int x = playerChunkX + dist; x > playerChunkX - dist; x--)
            for (int z = playerChunkZ + dist; z > playerChunkZ - dist; z--)
                clientNeededChunks.Add((x, z));

        clientUnneededChunks = clientUnneededChunks.Except(clientNeededChunks).ToList();
        clientNeededChunks = clientNeededChunks.Except(client.LoadedChunks).ToList();
        clientNeededChunks.Sort((chunk1, chunk2) =>
        {
            return Math.Abs(playerChunkX - chunk1.X) +
            Math.Abs(playerChunkZ - chunk1.Z) <
            Math.Abs(playerChunkX - chunk2.X) +
            Math.Abs(playerChunkZ - chunk2.Z) ? -1 : 1;
        });

        clientUnneededChunks.ForEach(c => client.LoadedChunks.TryRemove(c));

        foreach (var (X, Z) in clientNeededChunks)
        {
            var chunk = await world.GetChunkAsync(X, Z);
            if (chunk is not null && chunk.IsGenerated)
            {
                await client.SendChunkAsync(chunk);
                client.LoadedChunks.Add((chunk.X, chunk.Z));
            }
            else
            {
                sentAll = false;
            }
        }

        return sentAll;
    }

    private void WriteItems(NbtWriter writer, bool inventory = true)
    {
        var items = inventory ? Inventory.Select((item, slot) => (item, slot)) : EnderInventory.Select((item, slot) => (item, slot));

        var nonNullItems = items.Where(x => x.item != null);

        writer.WriteListStart(inventory ? "Inventory" : "EnderItems", NbtTagType.Compound, nonNullItems.Count());

        foreach (var (item, slot) in nonNullItems)
        {
            writer.WriteCompoundStart();

            writer.WriteByte("Count", (byte)item.Count);
            writer.WriteByte("Slot", (byte)slot);

            writer.WriteString("id", item.AsItem().UnlocalizedName);

            writer.WriteCompoundStart("tag");

            writer.WriteInt("Damage", item.ItemMeta.Durability);
            writer.WriteBool("Unbreakable", item.ItemMeta.Unbreakable);

            //TODO: item attributes

            writer.EndCompound();
            writer.EndCompound();
        }

        if (!nonNullItems.Any())
            writer.Write(NbtTagType.End);

        writer.EndList();
    }

    private Dictionary<Guid, List<InfoAction>> CompilePlayerInfo(params InfoAction[] actions) => new()
    {
        { Uuid, actions.ToList() }
    };

    private string GetPlayerDataPath(bool isOld = false) => Path.Join(world.PlayerDataPath, isOld ? $"{Uuid}.dat.old" : $"{Uuid}.dat");
}
