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

namespace Obsidian.Entities;

[MinecraftEntity("minecraft:player")]
public sealed partial class Player : Living, IPlayer
{
    private byte containerId = 0;

    public IClient Client { get; internal set; }

    public ILogger Logger => this.Client.Logger;

    internal HashSet<IPlayer> visiblePlayers = [];

    public bool IsDragging { get; set; }

    public int TeleportId { get; set; }

    // <summary>
    /// Which chunks the player should have loaded around them.
    /// </summary>
    public ConcurrentHashSet<long> LoadedChunks { get; internal set; } = [];

    public string Username { get; }

    public ClientInformation ClientInformation { get; set; }

    public required IServer Server { get; init; }

    public PlayerInput Input { get; set; }

    public override bool Sneaking
    {
        get => this.Input.HasFlag(PlayerInput.Sneak);
        set
        {
            if(value)
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

    public ItemStack? LastClickedItem { get; set; }

    public IBlock? LastClickedBlock { get; internal set; }

    public Gamemode Gamemode
    {
        get => field;
        set
        {
            field = value;

            Abilities = Gamemode switch
            {
                Gamemode.Creative => PlayerAbility.CreativeMode | PlayerAbility.AllowFlying | PlayerAbility.Invulnerable,
                Gamemode.Spectator => PlayerAbility.AllowFlying | PlayerAbility.Invulnerable,
                Gamemode.Survival or Gamemode.Adventure or Gamemode.Hardcore => PlayerAbility.None,
                _ => throw new ArgumentOutOfRangeException(nameof(Gamemode), Gamemode, "Unknown gamemode.")
            };
        }
    }

    public PlayerAbility Abilities { get; set; }

    public IScoreboard? CurrentScoreboard { get; set; }

    public bool Sleeping { get; set; }
    public bool InHorseInventory { get; set; }
    public bool Respawning { get; internal set; }

    public short AttackTime { get; set; }
    public short DeathTime { get; set; }
    public short HurtTime { get; set; }
    public short SleepTimer { get; set; }

    public short CurrentHeldItemSlot
    {
        get => (short)(field > 0 ? field - 36 : field);
        set
        {
            if (value is < 0 or > 8)
                throw new IndexOutOfRangeException("Value must be >= 0 or <= 8");

            field = (short)(value + 36);
        }
    }

    public int Ping => Client.Ping;
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

        World = world;
        Type = EntityType.Player;

        PersistentDataFile = Path.Combine(Obsidian.Server.PersistentDataPath, $"{Uuid}.dat");
        PersistentDataBackupFile = Path.Combine(Obsidian.Server.PersistentDataPath, $"{Uuid}.dat.old");

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

    public async override ValueTask TeleportAsync(VectorF pos)
    {
        LastPosition = Position;
        Position = pos;
        await UpdateChunksAsync(true, 2);

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

        await Client.QueuePacketAsync(new PlayerPositionPacket
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

        World.TryRemovePlayer(this);
        w.TryAddPlayer(this);

        World = w;

        // resync player data
        await LoadAsync(false);

        // reload world stuff and send rest of the info
        await UpdateChunksAsync(true, 2);

        await SendPlayerInfoAsync();

        var (chunkX, chunkZ) = Position.ToChunkCoord();
        await Client.QueuePacketAsync(new SetChunkCacheCenterPacket(chunkX, chunkZ));
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

    public async Task RespawnAsync(DataKept dataKept = DataKept.Metadata)
    {
        if (!Alive)
        {
            // if unalive, reset health and set location to world spawn
            Health = 20f;
            Position = World.LevelData.SpawnPosition;
        }

        CodecRegistry.TryGetDimension(World.DimensionName, out var codec);
        Debug.Assert(codec is not null); // TODO Handle missing codec

        Logger.LogDebug("Loading into world: {}", World.Name);

        await Client.QueuePacketAsync(new RespawnPacket
        {
            CommonPlayerSpawnInfo = new()
            {
                DimensionType = codec.Id,
                DimensionName = World.DimensionName,
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

        await Client.QueuePacketAsync(new PlayerPositionPacket
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

        await Client.QueuePacketAsync(new GameEventPacket(RespawnReason.EnableRespawnScreen));
        await RemoveAsync();

        if (source is Player attacker)
            attacker.visiblePlayers.Remove(this);
    }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(15, EntityMetadataType.Float);
        writer.WriteSingle(AdditionalHearts);

        writer.WriteEntityMetadataType(16, EntityMetadataType.VarInt);
        writer.WriteVarInt(XpTotal);

        writer.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        writer.WriteByte(ClientInformation.DisplayedSkinParts);

        writer.WriteEntityMetadataType(18, EntityMetadataType.Byte);
        writer.WriteByte(ClientInformation.MainHand);

        //TODO fix possibly an extension method?
        if (LeftShoulder is not null)
        {
            writer.WriteEntityMetadataType(19, EntityMetadataType.CompoundTag);
            writer.WriteNbtCompound([]);
        }

        if (RightShoulder is not null)
        {
            writer.WriteEntityMetadataType(20, EntityMetadataType.CompoundTag);
            writer.WriteNbtCompound([]);
        }
    }

    public async ValueTask SetGamemodeAsync(Gamemode gamemode)
    {
        this.PacketBroadcaster.QueuePacketToWorld(this.World, new PlayerInfoUpdatePacket(CompilePlayerInfo(new UpdateGamemodeInfoAction(gamemode))));

        await Client.QueuePacketAsync(new GameEventPacket(gamemode));

        Gamemode = gamemode;
    }

    public ValueTask UpdateDisplayNameAsync(string newDisplayName)
    {
        this.PacketBroadcaster.QueuePacketToWorld(this.World, new PlayerInfoUpdatePacket(CompilePlayerInfo(new UpdateDisplayNameInfoAction(newDisplayName))));

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

    public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.Count(x => HasPermission(x)) == permissions.Count();

    public byte GetNextContainerId()
    {
        containerId = (byte)(containerId % 255 + 1);

        return containerId;
    }

    public override string ToString() => Username;

    public async override ValueTask UpdateAsync(VectorF position, MovementFlags movementFlags)
    {
        await base.UpdateAsync(position, movementFlags);

        HeadY = position.Y + 1.62f;

        await TrySpawnPlayerAsync(position);

        await PickupNearbyItemsAsync();
    }

    public async override ValueTask UpdateAsync(VectorF position, Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        await base.UpdateAsync(position, yaw, pitch, movementFlags);

        HeadY = position.Y + 1.62f;

        await TrySpawnPlayerAsync(position);

        await PickupNearbyItemsAsync();
    }

    public async override ValueTask UpdateAsync(Angle yaw, Angle pitch, MovementFlags movementFlags)
    {
        await base.UpdateAsync(yaw, pitch, movementFlags);

        await PickupNearbyItemsAsync();
    }

    public async ValueTask DisconnectAsync(ChatMessage reason) => await this.Client.DisconnectAsync(reason);

    /// <summary>
    /// Updates client chunks. Only send <paramref name="distance"/> when sending initial chunks.
    /// </summary>
    /// <param name="unloadAll"></param>
    /// <param name="distance"></param>
    /// <returns>Whether all chunks have been sent.</returns>
    public async Task<bool> UpdateChunksAsync(bool unloadAll = false, int distance = 0)
    {
        bool sentAll = true;
        if (unloadAll)
        {
            if (!Respawning)
            {
                foreach (var value in LoadedChunks)
                {
                    NumericsHelper.LongToInts(value, out var x, out var z);
                    await UnloadChunkAsync(x, z);
                }
            }

            LoadedChunks.Clear();
        }

        List<long> clientNeededChunks = [];
        List<long> clientUnneededChunks = new(LoadedChunks);

        (int playerChunkX, int playerChunkZ) = Position.ToChunkCoord();

        int dist = distance < 1 ? ClientInformation.ViewDistance : distance;
        for (int x = playerChunkX + dist; x > playerChunkX - dist; x--)
            for (int z = playerChunkZ + dist; z > playerChunkZ - dist; z--)
                clientNeededChunks.Add(NumericsHelper.IntsToLong(x, z));

        clientUnneededChunks = clientUnneededChunks.Except(clientNeededChunks).ToList();
        clientNeededChunks = clientNeededChunks.Except(LoadedChunks).ToList();
        clientNeededChunks.Sort((chunk1, chunk2) =>
        {
            NumericsHelper.LongToInts(chunk1, out var chunk1X, out var chunk1Z);
            NumericsHelper.LongToInts(chunk2, out var chunk2X, out var chunk2Z);

            return Math.Abs(playerChunkX - chunk1X) +
            Math.Abs(playerChunkZ - chunk1Z) <
            Math.Abs(playerChunkX - chunk2X) +
            Math.Abs(playerChunkZ - chunk2Z) ? -1 : 1;
        });

        clientUnneededChunks.ForEach(c => LoadedChunks.TryRemove(c));

        foreach (var value in clientNeededChunks)
        {
            NumericsHelper.LongToInts(value, out var x, out var z);
            var chunk = await World.GetChunkAsync(x, z);
            if (chunk is not null && chunk.IsGenerated)
            {
                await Client.QueuePacketAsync(new LevelChunkWithLightPacket(chunk));

                LoadedChunks.Add(NumericsHelper.IntsToLong(chunk.X, chunk.Z));
            }
            else
            {
                sentAll = false;
            }
        }

        return sentAll;
    }
}
