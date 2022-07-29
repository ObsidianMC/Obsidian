// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities.Registry;
using Obsidian.WorldData;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace Obsidian.Entities;

public class Player : Living, IPlayer
{
    private byte containerId = 0;

    internal readonly Client client;

    internal HashSet<int> visiblePlayers = new();

    //TODO: better name??
    internal short inventorySlot = 36;

    internal bool isDragging;

    internal int TeleportId { get; set; }

    public bool IsOperator => Server.Operators.IsOperator(this);

    public string Username { get; }

    /// <summary>
    /// The players inventory.
    /// </summary>
    public Container Inventory { get; }
    public Container EnderInventory { get; }

    public BaseContainer OpenedContainer { get; set; }

    public Vector? LastDeathLocation { get; set; }

    public ItemStack LastClickedItem { get; internal set; }

    public Block LastClickedBlock { get; internal set; }

    public PlayerBitMask PlayerBitMask { get; set; }
    public Gamemode Gamemode { get; set; }

    public Hand MainHand { get; set; } = Hand.MainHand;

    public IScoreboard CurrentScoreboard { get; set; }

    public bool Sleeping { get; set; }
    public bool InHorseInventory { get; set; }
    public bool Respawning { get; internal set; }

    public short AttackTime { get; set; }
    public short DeathTime { get; set; }
    public short HurtTime { get; set; }
    public short SleepTimer { get; set; }

    public short CurrentSlot
    {
        get => (short)(this.inventorySlot - 36);
        internal set
        {
            if (value < 0 || value > 8)
                throw new IndexOutOfRangeException("Value must be >= 0 or <= 8");

            this.inventorySlot = (short)(value + 36);
        }
    }

    public int Ping => this.client.ping;
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

    public Entity LeftShoulder { get; set; }
    public Entity RightShoulder { get; set; }

    // Properties set by Obsidian (unofficial)
    // Not sure whether these should be saved to the NBT file.
    // These could be saved under nbt tags prefixed with "obsidian_"
    // As minecraft might just ignore them.
    public Permission PlayerPermissions { get; private set; } = new Permission("root");

    public string PersistentDataFile { get; }
    public string PersistentDataBackupFile { get; }

    public IPAddress? ClientIP => (client.RemoteEndPoint as IPEndPoint)?.Address;

    internal Player(Guid uuid, string username, Client client, World world)
    {
        this.Uuid = uuid;
        this.Username = username;
        this.client = client;
        this.EntityId = client.id;
        this.Inventory = new Container(9 * 5 + 1, InventoryType.Generic)
        {
            Owner = uuid,
            IsPlayerInventory = true
        };
        this.EnderInventory = new Container
        {
            Title = "Ender Chest"
        };

        this.World = world;
        this.Server = client.Server;
        this.Type = EntityType.Player;

        this.PersistentDataFile = Path.Join(this.server.PersistentDataPath, $"{this.Uuid}.dat");
        this.PersistentDataBackupFile = Path.Join(this.server.PersistentDataPath, $"{this.Uuid}.dat.old");
    }

    public ItemStack GetHeldItem() => this.Inventory.GetItem(this.inventorySlot);
    public ItemStack GetOffHandItem() => this.Inventory.GetItem(45);

    public async Task LoadPermsAsync()
    {
        // Load a JSON file that contains all permissions
        var file = new FileInfo(Path.Combine(this.server.PermissionPath, $"{this.Uuid}.json"));

        if (file.Exists)
        {
            await using var fs = file.OpenRead();

            this.PlayerPermissions = await fs.FromJsonAsync<Permission>();
        }
    }

    public async Task SavePermsAsync()
    {
        // Save permissions to JSON file
        var file = new FileInfo(Path.Combine(this.server.PermissionPath, $"{this.Uuid}.json"));

        await using var fs = file.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);

        await this.PlayerPermissions.ToJsonAsync(fs);
    }

    public async Task DisplayScoreboardAsync(IScoreboard scoreboard, ScoreboardPosition position)
    {
        var actualBoard = (Scoreboard)scoreboard;

        if (actualBoard.Objective is null)
            throw new InvalidOperationException("You must create an objective for the scoreboard before displaying it.");

        this.CurrentScoreboard = actualBoard;

        await this.client.QueuePacketAsync(new UpdateObjectivesPacket
        {
            ObjectiveName = actualBoard.name,
            Mode = ScoreboardMode.Create,
            Value = actualBoard.Objective.Value,
            Type = actualBoard.Objective.DisplayType
        });

        foreach (var (_, score) in actualBoard.scores)
        {
            await this.client.QueuePacketAsync(new UpdateScorePacket
            {
                EntityName = score.DisplayText,
                ObjectiveName = actualBoard.name,
                Action = 0,
                Value = score.Value
            });
        }

        await this.client.QueuePacketAsync(new DisplayObjectivePacket
        {
            ScoreName = actualBoard.name,
            Position = position
        });
    }

    public async Task OpenInventoryAsync(BaseContainer container)
    {
        this.OpenedContainer = container;

        var nextId = this.GetNextContainerId();

        await this.client.QueuePacketAsync(new OpenScreenPacket(container, nextId));

        if (container.HasItems())
            await this.client.QueuePacketAsync(new SetContainerContentPacket(nextId, container.ToList()));
    }

    public async override Task TeleportAsync(VectorF pos)
    {
        this.LastPosition = this.Position;
        this.Position = pos;
        await this.UpdateChunksAsync(true);

        var tid = Globals.Random.Next(0, 999);

        await client.Server.Events.InvokePlayerTeleportedAsync(
            new PlayerTeleportEventArgs
            (
                this,
                this.Position,
                pos
            ));

        await this.client.QueuePacketAsync(new SynchronizePlayerPositionPacket
        {
            Position = pos,
            Flags = PositionFlags.None,
            TeleportId = tid
        });
        this.TeleportId = tid;
    }

    public async override Task TeleportAsync(IEntity to)
    {
        this.LastPosition = this.Position;
        this.Position = to.Position;

        await this.UpdateChunksAsync(true);

        this.TeleportId = Globals.Random.Next(0, 999);

        await this.client.QueuePacketAsync(new SynchronizePlayerPositionPacket
        {
            Position = to.Position,
            Flags = PositionFlags.None,
            TeleportId = this.TeleportId
        });
    }

    public async override Task TeleportAsync(IWorld world)
    {
        if (world is not World w)
        {
            await base.TeleportAsync(world);
            return;
        }

        // save current world/persistent data 
        await this.SaveAsync();

        this.World.TryRemovePlayer(this);
        w.TryAddPlayer(this);

        this.World = w;

        // resync player data
        await this.LoadAsync(false);

        // reload world stuff and send rest of the info
        await this.UpdateChunksAsync(true);

        await this.client.SendInfoAsync();

        var (chunkX, chunkZ) = this.Position.ToChunkCoord();
        await this.client.QueuePacketAsync(new SetCenterChunkPacket(chunkX, chunkZ));
    }

    public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null, SecureMessageSignature? messageSignature = null) =>
        this.SendMessageAsync(ChatMessage.Simple(message), type, sender, messageSignature);

    public async Task SendMessageAsync(ChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null, SecureMessageSignature? messageSignature = null)
    {
        if (messageSignature.HasValue)
        {
            await client.QueuePacketAsync(new PlayerChatMessagePacket(message, type, sender)
            {
                SenderDisplayName = messageSignature?.Username ?? string.Empty,
                Salt = messageSignature?.Salt ?? 0,
                MessageSignature = messageSignature?.Value ?? Array.Empty<byte>(),
                UnsignedChatMessage = message,
                Timestamp = messageSignature?.Timestamp ?? DateTimeOffset.UtcNow,
            });

            return;
        }

        await client.QueuePacketAsync(new SystemChatMessagePacket(message, type));
    }

    public Task SendEntitySoundAsync(Sounds soundId, int entityId, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
        client.QueuePacketAsync(new EntitySoundEffectPacket(soundId, entityId, category, volume, pitch));

    public Task SendSoundAsync(Sounds soundId, SoundPosition soundPosition, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
        client.QueuePacketAsync(new SoundEffectPacket(soundId, soundPosition, category, volume, pitch));

    public Task SendCustomSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
        client.QueuePacketAsync(new CustomSoundEffectPacket(name, position, category, volume, pitch));

    public Task KickAsync(string reason) => this.client.DisconnectAsync(ChatMessage.Simple(reason));
    public Task KickAsync(ChatMessage reason) => this.client.DisconnectAsync(reason);

    public async Task RespawnAsync(bool copyMetadata = false)
    {
        if (!Alive)
        {
            // if unalive, reset health and set location to world spawn
            this.Health = 20f;
            this.Position = this.World.LevelData.SpawnPosition;
        }

        Registry.TryGetDimensionCodec(this.World.DimensionName, out var codec);

        this.server.Logger.LogDebug("Loading into world: {}", this.World.Name);

        await this.client.QueuePacketAsync(new RespawnPacket
        {
            Dimension = codec,
            DimensionName = this.World.DimensionName,
            Gamemode = this.Gamemode,
            PreviousGamemode = this.Gamemode,
            HashedSeed = 0,
            IsFlat = false,
            IsDebug = false,
            CopyMetadata = copyMetadata
        });

        this.visiblePlayers.Clear();

        this.Respawning = true;

        await this.UpdateChunksAsync(true);

        await this.client.QueuePacketAsync(new SynchronizePlayerPositionPacket
        {
            Position = this.Position,
            Yaw = 0,
            Pitch = 0,
            Flags = PositionFlags.None,
            TeleportId = 0
        });

        this.Respawning = false;
    }

    //TODO make IDamageSource 
    public async override Task KillAsync(IEntity source, ChatMessage deathMessage)
    {
        //await this.client.QueuePacketAsync(new PlayerDied
        //{
        //    PlayerId = this.EntityId,
        //    EntityId = source != null ? source.EntityId : -1,
        //    Message = deathMessage as ChatMessage
        //});
        // TODO implement new death packets

        await this.client.QueuePacketAsync(new GameEventPacket(RespawnReason.EnableRespawnScreen));
        await this.RemoveAsync();

        if (source is Player attacker)
            attacker.visiblePlayers.Remove(this.EntityId);
    }

    public async override Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(14, EntityMetadataType.Float, this.AdditionalHearts);

        await stream.WriteEntityMetdata(15, EntityMetadataType.VarInt, this.XpP);

        await stream.WriteEntityMetdata(16, EntityMetadataType.Byte, (byte)this.PlayerBitMask);

        await stream.WriteEntityMetdata(17, EntityMetadataType.Byte, (byte)this.MainHand);

        if (this.LeftShoulder != null)
            await stream.WriteEntityMetdata(18, EntityMetadataType.Nbt, this.LeftShoulder);

        if (this.RightShoulder != null)
            await stream.WriteEntityMetdata(19, EntityMetadataType.Nbt, this.RightShoulder);

        await stream.WriteEntityMetdata(20, EntityMetadataType.OptPosition, this.LastDeathLocation.Value, !this.LastDeathLocation.HasValue);
    }

    public override void Write(MinecraftStream stream)
    {
        base.Write(stream);

        stream.WriteEntityMetadataType(14, EntityMetadataType.Float);
        stream.WriteFloat(AdditionalHearts);

        stream.WriteEntityMetadataType(15, EntityMetadataType.VarInt);
        stream.WriteVarInt(XpTotal);

        stream.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        stream.WriteByte((byte)PlayerBitMask);

        stream.WriteEntityMetadataType(17, EntityMetadataType.Byte);
        stream.WriteByte((byte)MainHand);

        if (LeftShoulder is not null)
        {
            stream.WriteEntityMetadataType(18, EntityMetadataType.Nbt);
            stream.WriteVarInt(LeftShoulder);
        }

        if (RightShoulder is not null)
        {
            stream.WriteEntityMetadataType(19, EntityMetadataType.Nbt);
            stream.WriteVarInt(RightShoulder);
        }

        if (this.LastDeathLocation is not null)
        {
            stream.WriteEntityMetadataType(20, EntityMetadataType.OptPosition);
            stream.WritePosition(this.LastDeathLocation.Value);
        }
    }

    public async Task SetGamemodeAsync(Gamemode gamemode)
    {
        var list = new List<InfoAction>()
            {
                new UpdateGamemodeInfoAction()
                {
                    Uuid = this.Uuid,
                    Gamemode = (int)gamemode,
                }
            };

        await this.client.Server.QueueBroadcastPacketAsync(new PlayerInfoPacket(PlayerInfoAction.UpdateGamemode, list));
        await this.client.QueuePacketAsync(new GameEventPacket(gamemode));

        this.Gamemode = gamemode;
    }

    public async Task UpdateDisplayNameAsync(string newDisplayName)
    {
        var list = new List<InfoAction>()
            {
                new UpdateDisplayNameInfoAction()
                {
                    Uuid = this.Uuid,
                    DisplayName = newDisplayName,
                }
            };

        await this.client.Server.QueueBroadcastPacketAsync(new PlayerInfoPacket(PlayerInfoAction.UpdateDisplayName, list));

        this.CustomName = newDisplayName;
    }

    public async Task SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new SetTitleTextPacket(TitleMode.SetTitle)
        {
            Text = title
        };

        var titleTimesPacket = new SetTitleAnimationTimesPacket
        {
            FadeIn = fadeIn,
            FadeOut = fadeOut,
            Stay = stay,
        };

        await this.client.QueuePacketAsync(titlePacket);
        await this.client.QueuePacketAsync(titleTimesPacket);
    }

    public async Task SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new SetTitleTextPacket(TitleMode.SetSubtitle)
        {
            Text = subtitle
        };

        await this.client.QueuePacketAsync(titlePacket);

        await this.SendTitleAsync(title, fadeIn, stay, fadeOut);
    }

    public async Task SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new SetTitleTextPacket(TitleMode.SetSubtitle)
        {
            Text = subtitle
        };

        var titleTimesPacket = new SetTitleAnimationTimesPacket
        {
            FadeIn = fadeIn,
            FadeOut = fadeOut,
            Stay = stay,
        };

        await this.client.QueuePacketAsync(titlePacket);
        await this.client.QueuePacketAsync(titleTimesPacket);
    }

    public async Task SendActionBarAsync(string text)
    {
        var actionBarPacket = new SetActionBarTextPacket
        {
            Text = text
        };

        await this.client.QueuePacketAsync(actionBarPacket);
    }

    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float extra = 0) =>
        SpawnParticleAsync(particle, new VectorF(x, y, z), count, extra);

    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float offsetX,
        float offsetY, float offsetZ, float extra = 0) =>
        SpawnParticleAsync(particle, new VectorF(x, y, z), count, offsetX, offsetY, offsetZ, extra);


    public async Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float extra = 0) =>
        await this.client.QueuePacketAsync(new ParticlePacket(particle, pos, count) { MaxSpeed = extra });

    public async Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float offsetX, float offsetY,
        float offsetZ, float extra = 0) => await this.client.QueuePacketAsync(
        new ParticlePacket(particle, pos, count) { Offset = new VectorF(offsetX, offsetY, offsetZ), MaxSpeed = extra });

    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, ParticleData data,
        float extra = 0) =>
        SpawnParticleAsync(particle, new VectorF(x, y, z), count, extra);

    public Task SpawnParticleAsync(ParticleType particle, float x, float y, float z, int count, float offsetX, float offsetY, float offsetZ, ParticleData data, float extra = 0) =>
        SpawnParticleAsync(particle, new VectorF(x, y, z), count, offsetX, offsetY, offsetZ, extra);

    public async Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, ParticleData data,
        float extra = 0) =>
        await this.client.QueuePacketAsync(new ParticlePacket(particle, pos, count) { Data = data, MaxSpeed = extra });

    public async Task SpawnParticleAsync(ParticleType particle, VectorF pos, int count, float offsetX, float offsetY,
        float offsetZ, ParticleData data, float extra = 0) => await this.client.QueuePacketAsync(
        new ParticlePacket(particle, pos, count)
        {
            Data = data,
            Offset = new VectorF(offsetX, offsetY, offsetZ),
            MaxSpeed = extra
        });

    public async Task SaveAsync()
    {
        var playerDataFile = new FileInfo(this.GetPlayerDataPath());
        var persistentDataFile = new FileInfo(this.PersistentDataFile);

        if (playerDataFile.Exists)
        {
            playerDataFile.CopyTo(this.GetPlayerDataPath(true), true);
            playerDataFile.Delete();
        }

        if (persistentDataFile.Exists)
        {
            persistentDataFile.CopyTo(this.PersistentDataBackupFile, true);
            persistentDataFile.Delete();
        }

        await using var persistentDataStream = persistentDataFile.Create();
        await using var persistentDataWriter = new NbtWriter(persistentDataStream, NbtCompression.GZip, "");

        persistentDataWriter.WriteString("worldName", this.World.ParentWorldName ?? this.World.Name);
        //TODO make sure to save inventory in the right location if has using global data set to true

        persistentDataWriter.EndCompound();
        await persistentDataWriter.TryFinishAsync();

        await using var playerFileStream = playerDataFile.Create();
        await using var writer = new NbtWriter(playerFileStream, NbtCompression.GZip, "");

        writer.WriteInt("DataVersion", 2724);
        writer.WriteInt("playerGameType", (int)this.Gamemode);
        writer.WriteInt("previousPlayerGameType", (int)this.Gamemode);
        writer.WriteInt("Score", 0);
        writer.WriteInt("SelectedItemSlot", this.inventorySlot);
        writer.WriteInt("foodLevel", this.FoodLevel);
        writer.WriteInt("foodTickTimer", this.FoodTickTimer);
        writer.WriteInt("XpLevel", this.XpLevel);
        writer.WriteInt("XpTotal", this.XpTotal);

        writer.WriteFloat("Health", this.Health);

        writer.WriteFloat("foodExhaustionLevel", this.FoodExhaustionLevel);
        writer.WriteFloat("foodSaturationLevel", this.FoodSaturationLevel);

        writer.WriteString("Dimension", this.World.DimensionName);

        writer.WriteListStart("Pos", NbtTagType.Double, 3);

        writer.WriteDouble(this.Position.X);
        writer.WriteDouble(this.Position.Y);
        writer.WriteDouble(this.Position.Z);

        writer.EndList();

        writer.WriteListStart("Rotation", NbtTagType.Float, 2);

        writer.WriteFloat(this.Yaw);
        writer.WriteFloat(this.Pitch);

        writer.EndList();

        this.WriteItems(writer);
        this.WriteItems(writer, false);

        writer.EndCompound();

        await writer.TryFinishAsync();
    }

    public async Task LoadAsync(bool loadFromPersistentWorld = true)
    {
        // Read persistent data first
        var persistentDataFile = new FileInfo(this.PersistentDataFile);

        if (persistentDataFile.Exists)
        {
            await using var persistentDataStream = persistentDataFile.OpenRead();

            var persistentDataReader = new NbtReader(persistentDataStream, NbtCompression.GZip);

            //TODO use inventory if has using global data set to true
            if (persistentDataReader.ReadNextTag() is NbtCompound persistentDataCompound)
            {
                var worldName = persistentDataCompound.GetString("worldName");

                this.server.Logger.LogInformation($"persistent world: {worldName}");

                if (loadFromPersistentWorld && this.server.WorldManager.TryGetWorld(worldName, out var world))
                {
                    this.World = world;
                    this.server.Logger.LogInformation($"Loading from persistent world: {worldName}");
                }
            }
        }

        // Then read player data
        var playerDataFile = new FileInfo(this.GetPlayerDataPath());

        await this.LoadPermsAsync();

        if (!playerDataFile.Exists)
        {
            this.Position = new VectorF(0, 128, 0);
            return;
        }

        await using var playerFileStream = playerDataFile.OpenRead();

        var reader = new NbtReader(playerFileStream, NbtCompression.GZip);

        var compound = reader.ReadNextTag() as NbtCompound;

        this.OnGround = compound.GetBool("OnGround");
        this.Sleeping = compound.GetBool("Sleeping");
        this.Air = compound.GetShort("Air");
        this.AttackTime = compound.GetShort("AttackTime");
        this.DeathTime = compound.GetShort("DeathTime");
        this.Health = compound.GetFloat("Health");
        this.HurtTime = compound.GetShort("HurtTime");
        this.SleepTimer = compound.GetShort("SleepTimer");
        this.FoodLevel = compound.GetInt("foodLevel");
        this.FoodTickTimer = compound.GetInt("foodTickTimer");
        this.Gamemode = (Gamemode)compound.GetInt("playerGameType");
        this.XpLevel = compound.GetInt("XpLevel");
        this.XpTotal = compound.GetInt("XpTotal");
        this.FallDistance = compound.GetFloat("FallDistance");
        this.FoodExhaustionLevel = compound.GetFloat("foodExhaustionLevel");
        this.FoodSaturationLevel = compound.GetFloat("foodSaturationLevel");
        this.XpP = compound.GetInt("XpP");

        var dimensionName = compound.GetString("Dimension");
        if (!string.IsNullOrWhiteSpace(dimensionName) && Registry.TryGetDimensionCodec(dimensionName, out var codec))
        {
            //TODO load into dimension ^ ^
        }

        if (compound.TryGetTag("Pos", out var posTag))
        {
            var list = posTag as NbtList;

            var pos = list.Select(x => x as NbtTag<double>).ToList();

            float x = (float)pos[0].Value,
            y = (float)pos[1].Value,
            z = (float)pos[2].Value;

            this.Position = new VectorF(x, y, z);
        }
        else
            this.Position = this.World.LevelData.SpawnPosition;

        if (compound.TryGetTag("Rotation", out var rotationTag))
        {
            var list = rotationTag as NbtList;

            var rotation = list.Select(x => x as NbtTag<float>).ToList();

            this.Yaw = rotation[0].Value;
            this.Pitch = rotation[1].Value;
        }

        if (compound.TryGetTag("Inventory", out var rawTag))
        {
            var inventory = rawTag as NbtList;

            foreach (var rawItemTag in inventory)
            {
                if (rawItemTag.Type == NbtTagType.End)
                    break;

                var itemCompound = rawItemTag as NbtCompound;

                var slot = itemCompound.GetByte("Slot");

                var itemMetaBuilder = new ItemMetaBuilder()
                    .WithDurability(itemCompound.GetInt("Damage"))
                    .IsUnbreakable(itemCompound.GetBool("Unbreakable"));

                var item = Registry.GetSingleItem(itemCompound.GetString("id"), itemMetaBuilder.Build());
                item.Count = itemCompound.GetByte("Count");
                item.Slot = slot;

                this.Inventory.SetItem(slot, item);
            }
        }
    }

    public async Task<bool> GrantPermissionAsync(string permissionNode)
    {
        var permissions = permissionNode.ToLower().Trim().Split('.');

        var parent = this.PlayerPermissions;
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

        await this.SavePermsAsync();

        if (result)
            await this.client.Server.Events.InvokePermissionGrantedAsync(new PermissionGrantedEventArgs(this, permissionNode));

        return result;
    }

    public async Task<bool> RevokePermissionAsync(string permissionNode)
    {
        var permissions = permissionNode.ToLower().Trim().Split('.');

        // Set root node and whether we created a new permission (still false)
        var parent = this.PlayerPermissions;

        foreach (var permission in permissions)
        {
            if (parent.Children.Any(x => x.Name.EqualsIgnoreCase(permission)))
            {
                // child exists remove them
                var childToRemove = parent.Children.First(x => x.Name.EqualsIgnoreCase(permission));

                parent.Children.Remove(childToRemove);

                await this.SavePermsAsync();
                await this.client.Server.Events.InvokePermissionRevokedAsync(new PermissionRevokedEventArgs(this, permissionNode));

                return true;
            }
        }

        return false;
    }

    public bool HasPermission(string permissionNode)
    {
        if (this.PlayerPermissions.Children.Count == 0)
            return false;

        var permissions = permissionNode.ToLower().Trim().Split('.');

        var parent = this.PlayerPermissions;

        if (parent.Children.Count <= 0)
            return false;

        foreach (var permission in permissions)
        {
            if (parent.Children.Any(x => x.Name == Permission.Wildcard) || parent.Children.Any(x => x.Name.EqualsIgnoreCase(permission)))
                return true;

            parent = parent.Children.First(x => x.Name.EqualsIgnoreCase(permission));
        }

        return false;
    }

    public bool HasAnyPermission(IEnumerable<string> permissions) => permissions.Any(x => this.HasPermission(x));

    public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.Count(x => this.HasPermission(x)) == permissions.Count();

    public byte GetNextContainerId()
    {
        this.containerId = (byte)(this.containerId % 255 + 1);

        return this.containerId;
    }

    public override string ToString() => this.Username;

    internal async override Task UpdateAsync(VectorF position, bool onGround)
    {
        await base.UpdateAsync(position, onGround);

        this.HeadY = position.Y + 1.62f;

        await this.TrySpawnPlayerAsync(position);

        await this.PickupNearbyItemsAsync(1);
    }

    internal async override Task UpdateAsync(VectorF position, Angle yaw, Angle pitch, bool onGround)
    {
        await base.UpdateAsync(position, yaw, pitch, onGround);

        this.HeadY = position.Y + 1.62f;

        await this.TrySpawnPlayerAsync(position);

        await this.PickupNearbyItemsAsync(0.8f);
    }

    internal async override Task UpdateAsync(Angle yaw, Angle pitch, bool onGround)
    {
        await base.UpdateAsync(yaw, pitch, onGround);

        await this.PickupNearbyItemsAsync(2);
    }

    private async Task TrySpawnPlayerAsync(VectorF position)
    {
        foreach (var (_, player) in this.World.Players.Except(this.Uuid).Where(x => VectorF.Distance(position, x.Value.Position) <= (x.Value.client.ClientSettings?.ViewDistance ?? 10)))
        {
            if (player.Alive && !this.visiblePlayers.Contains(player.EntityId))
            {
                this.visiblePlayers.Add(player.EntityId);

                await this.client.QueuePacketAsync(new SpawnPlayerPacket
                {
                    EntityId = player.EntityId,
                    Uuid = player.Uuid,
                    Position = player.Position,
                    Yaw = player.Yaw,
                    Pitch = player.Pitch
                });
            }
        }

        var removed = this.visiblePlayers.Where(x => this.Server.GetPlayer(x) == null || !this.World.Players.Any(p => p.Value == x)).ToArray();
        this.visiblePlayers.RemoveWhere(x => this.Server.GetPlayer(x) == null || !this.World.Players.Any(p => p.Value == x));

        if (removed.Length > 0)
            await this.client.QueuePacketAsync(new RemoveEntitiesPacket(removed));
    }

    private async Task PickupNearbyItemsAsync(float distance = 0.5f)
    {
        foreach (var entity in this.World.GetEntitiesNear(this.Position, distance))
        {
            if (entity is ItemEntity item)
            {
                this.server.BroadcastPacket(new PickupItemPacket
                {
                    CollectedEntityId = item.EntityId,
                    CollectorEntityId = this.EntityId,
                    PickupItemCount = item.Count
                });

                var slot = this.Inventory.AddItem(new ItemStack(item.Material, item.Count, item.ItemMeta));

                this.client.SendPacket(new SetContainerSlotPacket
                {
                    Slot = (short)slot,

                    WindowId = 0,

                    SlotData = this.Inventory.GetItem(slot),

                    StateId = this.Inventory.StateId++
                });

                await item.RemoveAsync();
            }
        }
    }

    internal async Task UpdateChunksAsync(bool unloadAll = false)
    {
        if (unloadAll)
        {
            if (!Respawning)
            {
                foreach (var (X, Z) in client.LoadedChunks)
                    await client.UnloadChunkAsync(X, Z);
            }

            client.LoadedChunks.Clear();
        }

        List<(int X, int Z)> clientNeededChunks = new();
        List<(int X, int Z)> clientUnneededChunks = new(client.LoadedChunks);

        (int playerChunkX, int playerChunkZ) = Position.ToChunkCoord();
        (int lastPlayerChunkX, int lastPlayerChunkZ) = LastPosition.ToChunkCoord();

        int dist = (client.ClientSettings?.ViewDistance ?? 14) - 2;
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

        await Parallel.ForEachAsync(clientUnneededChunks, async (chunkLoc, _) =>
        {
            await client.UnloadChunkAsync(chunkLoc.X, chunkLoc.Z);
            client.LoadedChunks.TryRemove(chunkLoc);
        });

        await Parallel.ForEachAsync(clientNeededChunks, async (chunkLoc, _) =>
        {
            var chunk = await World.GetChunkAsync(chunkLoc.X, chunkLoc.Z);
            if (chunk is not null)
            {
                await client.SendChunkAsync(chunk);
                client.LoadedChunks.Add((chunk.X, chunk.Z));
            }
        });
    }

    private void WriteItems(NbtWriter writer, bool inventory = true)
    {
        var items = inventory ? this.Inventory.Select((item, slot) => (item, slot)) : this.EnderInventory.Select((item, slot) => (item, slot));

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

    private string GetPlayerDataPath(bool isOld = false) =>
        !isOld ? Path.Join(this.World.PlayerDataPath, $"{this.Uuid}.dat") : Path.Join(this.World.PlayerDataPath, $"{this.Uuid}.dat.old");
}
