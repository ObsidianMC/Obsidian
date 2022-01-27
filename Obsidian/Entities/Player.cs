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
        get => (short)(inventorySlot - 36);
        internal set
        {
            if (value < 0 || value > 8)
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

    public Entity LeftShoulder { get; set; }
    public Entity RightShoulder { get; set; }

    // Properties set by Obsidian (unofficial)
    // Not sure whether these should be saved to the NBT file.
    // These could be saved under nbt tags prefixed with "obsidian_"
    // As minecraft might just ignore them.
    public Permission PlayerPermissions { get; private set; } = new Permission("root");

    public string PersistentDataFile { get; }
    public string PersistentDataBackupFile { get; }

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

        World = world;
        Server = client.Server;
        Type = EntityType.Player;

        PersistentDataFile = Path.Join(server.PersistentDataPath, $"{Uuid}.dat");
        PersistentDataBackupFile = Path.Join(server.PersistentDataPath, $"{Uuid}.dat.old");
    }

    public ItemStack GetHeldItem() => Inventory.GetItem(inventorySlot);
    public ItemStack GetOffHandItem() => Inventory.GetItem(45);

    public async Task LoadPermsAsync()
    {
        // Load a JSON file that contains all permissions
        var file = new FileInfo(Path.Combine(server.PermissionPath, $"{Uuid}.json"));

        if (file.Exists)
        {
            await using var fs = file.OpenRead();

            PlayerPermissions = await fs.FromJsonAsync<Permission>();
        }
    }

    public async Task SavePermsAsync()
    {
        // Save permissions to JSON file
        var file = new FileInfo(Path.Combine(server.PermissionPath, $"{Uuid}.json"));

        await using var fs = file.Open(FileMode.OpenOrCreate, FileAccess.ReadWrite);

        await PlayerPermissions.ToJsonAsync(fs);
    }

    public async Task DisplayScoreboardAsync(IScoreboard scoreboard, ScoreboardPosition position)
    {
        var actualBoard = (Scoreboard)scoreboard;

        if (actualBoard.Objective is null)
            throw new InvalidOperationException("You must create an objective for the scoreboard before displaying it.");

        CurrentScoreboard = actualBoard;

        await client.QueuePacketAsync(new ScoreboardObjectivePacket
        {
            ObjectiveName = actualBoard.name,
            Mode = ScoreboardMode.Create,
            Value = actualBoard.Objective.Value,
            Type = actualBoard.Objective.DisplayType
        });

        foreach (var (_, score) in actualBoard.scores)
        {
            await client.QueuePacketAsync(new UpdateScore
            {
                EntityName = score.DisplayText,
                ObjectiveName = actualBoard.name,
                Action = 0,
                Value = score.Value
            });
        }

        await client.QueuePacketAsync(new DisplayScoreboard
        {
            ScoreName = actualBoard.name,
            Position = position
        });
    }

    public async Task OpenInventoryAsync(BaseContainer container)
    {
        OpenedContainer = container;

        var nextId = GetNextContainerId();

        await client.QueuePacketAsync(new OpenWindow(container, nextId));

        if (container.HasItems())
            await client.QueuePacketAsync(new WindowItems(nextId, container.ToList()));
    }

    public async override Task TeleportAsync(VectorF pos)
    {
        LastPosition = Position;
        Position = pos;
        await client.UpdateChunksAsync(true);

        var tid = Globals.Random.Next(0, 999);

        await client.Server.Events.InvokePlayerTeleportedAsync(
            new PlayerTeleportEventArgs
            (
                this,
                Position,
                pos
            ));

        await client.QueuePacketAsync(new PlayerPositionAndLook
        {
            Position = pos,
            Flags = PositionFlags.None,
            TeleportId = tid
        });
        TeleportId = tid;
    }

    public async override Task TeleportAsync(IEntity to)
    {
        LastPosition = Position;
        Position = to.Position;

        await client.UpdateChunksAsync(true);

        TeleportId = Globals.Random.Next(0, 999);

        await client.QueuePacketAsync(new PlayerPositionAndLook
        {
            Position = to.Position,
            Flags = PositionFlags.None,
            TeleportId = TeleportId
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
        await SaveAsync();

        World.TryRemovePlayer(this);
        w.TryAddPlayer(this);

        World = w;

        // resync player data
        await LoadAsync(false);

        // reload world stuff and send rest of the info
        await client.UpdateChunksAsync(true);

        await client.SendInfoAsync();

        var (chunkX, chunkZ) = Position.ToChunkCoord();
        await client.QueuePacketAsync(new UpdateViewPosition(chunkX, chunkZ));
    }

    public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null) =>
        SendMessageAsync(ChatMessage.Simple(message), type, sender ?? Guid.Empty);

    public Task SendMessageAsync(ChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null) =>
        client.QueuePacketAsync(new ChatMessagePacket(message, type, sender ?? Guid.Empty));

    public Task SendSoundAsync(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
        client.QueuePacketAsync(new SoundEffect(soundId, position, category, volume, pitch));

    public Task SendNamedSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
        client.QueuePacketAsync(new NamedSoundEffect(name, position, category, volume, pitch));

    public Task KickAsync(string reason) => client.DisconnectAsync(ChatMessage.Simple(reason));
    public Task KickAsync(ChatMessage reason) => client.DisconnectAsync(reason);

    public async Task RespawnAsync(bool copyMetadata = false)
    {
        if (!Alive)
        {
            // if unalive, reset health and set location to world spawn
            Health = 20f;
            Position = World.LevelData.SpawnPosition;
        }

        Registry.TryGetDimensionCodec(World.DimensionName, out var codec);

        server.Logger.LogDebug("Loading into world: {}", World.Name);

        await client.QueuePacketAsync(new Respawn
        {
            Dimension = codec,
            DimensionName = World.DimensionName,
            Gamemode = Gamemode,
            PreviousGamemode = Gamemode,
            HashedSeed = 0,
            IsFlat = false,
            IsDebug = false,
            CopyMetadata = copyMetadata
        });

        visiblePlayers.Clear();

        Respawning = true;

        await client.UpdateChunksAsync(true);

        await client.QueuePacketAsync(new PlayerPositionAndLook
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
    public async override Task KillAsync(IEntity source, ChatMessage deathMessage)
    {
        //await this.client.QueuePacketAsync(new PlayerDied
        //{
        //    PlayerId = this.EntityId,
        //    EntityId = source != null ? source.EntityId : -1,
        //    Message = deathMessage as ChatMessage
        //});
        // TODO implement new death packets

        await client.QueuePacketAsync(new ChangeGameState(RespawnReason.EnableRespawnScreen));
        await RemoveAsync();

        if (source is Player attacker)
            attacker.visiblePlayers.Remove(EntityId);
    }

    public async override Task WriteAsync(MinecraftStream stream)
    {
        await base.WriteAsync(stream);

        await stream.WriteEntityMetdata(14, EntityMetadataType.Float, AdditionalHearts);

        await stream.WriteEntityMetdata(15, EntityMetadataType.VarInt, XpP);

        await stream.WriteEntityMetdata(16, EntityMetadataType.Byte, (byte)PlayerBitMask);

        await stream.WriteEntityMetdata(17, EntityMetadataType.Byte, (byte)MainHand);

        if (LeftShoulder != null)
            await stream.WriteEntityMetdata(18, EntityMetadataType.Nbt, LeftShoulder);

        if (RightShoulder != null)
            await stream.WriteEntityMetdata(19, EntityMetadataType.Nbt, RightShoulder);
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
    }

    public async Task SetGamemodeAsync(Gamemode gamemode)
    {
        var list = new List<InfoAction>()
            {
                new UpdateGamemodeInfoAction()
                {
                    Uuid = Uuid,
                    Gamemode = (int)gamemode,
                }
            };

        await client.Server.QueueBroadcastPacketAsync(new PlayerInfoPacket(PlayerInfoAction.UpdateGamemode, list));
        await client.QueuePacketAsync(new ChangeGameState(gamemode));

        Gamemode = gamemode;
    }

    public async Task UpdateDisplayNameAsync(string newDisplayName)
    {
        var list = new List<InfoAction>()
            {
                new UpdateDisplayNameInfoAction()
                {
                    Uuid = Uuid,
                    DisplayName = newDisplayName,
                }
            };

        await client.Server.QueueBroadcastPacketAsync(new PlayerInfoPacket(PlayerInfoAction.UpdateDisplayName, list));

        CustomName = newDisplayName;
    }

    public async Task SendTitleAsync(ChatMessage title, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new TitlePacket(TitleMode.SetTitle)
        {
            Text = title
        };

        var titleTimesPacket = new TitleTimesPacket
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
        var titlePacket = new TitlePacket(TitleMode.SetSubtitle)
        {
            Text = subtitle
        };

        await client.QueuePacketAsync(titlePacket);

        await SendTitleAsync(title, fadeIn, stay, fadeOut);
    }

    public async Task SendSubtitleAsync(ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new TitlePacket(TitleMode.SetSubtitle)
        {
            Text = subtitle
        };

        var titleTimesPacket = new TitleTimesPacket
        {
            FadeIn = fadeIn,
            FadeOut = fadeOut,
            Stay = stay,
        };

        await client.QueuePacketAsync(titlePacket);
        await client.QueuePacketAsync(titleTimesPacket);
    }

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

        persistentDataWriter.WriteString("worldName", World.ParentWorldName ?? World.Name);
        //TODO make sure to save inventory in the right location if has using global data set to true

        persistentDataWriter.EndCompound();
        await persistentDataWriter.TryFinishAsync();

        await using var playerFileStream = playerDataFile.Create();
        await using var writer = new NbtWriter(playerFileStream, NbtCompression.GZip, "");

        writer.WriteInt("DataVersion", 2724);
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

        writer.WriteString("Dimension", World.DimensionName);

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
                var worldName = persistentDataCompound.GetString("worldName");

                server.Logger.LogInformation($"persistent world: {worldName}");

                if (loadFromPersistentWorld && server.WorldManager.TryGetWorld(worldName, out var world))
                {
                    World = world;
                    server.Logger.LogInformation($"Loading from persistent world: {worldName}");
                }
            }
        }

        // Then read player data
        var playerDataFile = new FileInfo(GetPlayerDataPath());

        await LoadPermsAsync();

        if (!playerDataFile.Exists)
        {
            Position = World.LevelData.SpawnPosition;
            return;
        }

        await using var playerFileStream = playerDataFile.OpenRead();

        var reader = new NbtReader(playerFileStream, NbtCompression.GZip);

        var compound = reader.ReadNextTag() as NbtCompound;

        OnGround = compound.GetBool("OnGround");
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

            Position = new VectorF(x, y, z);
        }
        else
            Position = World.LevelData.SpawnPosition;

        if (compound.TryGetTag("Rotation", out var rotationTag))
        {
            var list = rotationTag as NbtList;

            var rotation = list.Select(x => x as NbtTag<float>).ToList();

            Yaw = rotation[0].Value;
            Pitch = rotation[1].Value;
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
            await client.Server.Events.InvokePermissionGrantedAsync(new PermissionGrantedEventArgs(this, permissionNode));

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

                await SavePermsAsync();
                await client.Server.Events.InvokePermissionRevokedAsync(new PermissionRevokedEventArgs(this, permissionNode));

                return true;
            }
        }

        return false;
    }

    public bool HasPermission(string permissionNode)
    {
        if (PlayerPermissions.Children.Count == 0)
            return false;

        var permissions = permissionNode.ToLower().Trim().Split('.');

        var parent = PlayerPermissions;

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

    public bool HasAnyPermission(IEnumerable<string> permissions) => permissions.Any(x => HasPermission(x));

    public bool HasAllPermissions(IEnumerable<string> permissions) => permissions.Count(x => HasPermission(x)) == permissions.Count();

    public byte GetNextContainerId()
    {
        containerId = (byte)(containerId % 255 + 1);

        return containerId;
    }

    public override string ToString() => Username;

    internal async override Task UpdateAsync(VectorF position, bool onGround)
    {
        await base.UpdateAsync(position, onGround);

        HeadY = position.Y + 1.62f;

        await TrySpawnPlayerAsync(position);

        await PickupNearbyItemsAsync(1);
    }

    internal async override Task UpdateAsync(VectorF position, Angle yaw, Angle pitch, bool onGround)
    {
        await base.UpdateAsync(position, yaw, pitch, onGround);

        HeadY = position.Y + 1.62f;

        await TrySpawnPlayerAsync(position);

        await PickupNearbyItemsAsync(0.8f);
    }

    internal async override Task UpdateAsync(Angle yaw, Angle pitch, bool onGround)
    {
        await base.UpdateAsync(yaw, pitch, onGround);

        await PickupNearbyItemsAsync(2);
    }

    private async Task TrySpawnPlayerAsync(VectorF position)
    {
        foreach (var (_, player) in World.Players.Except(Uuid).Where(x => VectorF.Distance(position, x.Value.Position) <= (x.Value.client.ClientSettings?.ViewDistance ?? 10)))
        {
            if (player.Alive && !visiblePlayers.Contains(player.EntityId))
            {
                visiblePlayers.Add(player.EntityId);

                await client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = player.EntityId,
                    Uuid = player.Uuid,
                    Position = player.Position,
                    Yaw = player.Yaw,
                    Pitch = player.Pitch
                });
            }
        }

        var removed = visiblePlayers.Where(x => Server.GetPlayer(x) == null || !World.Players.Any(p => p.Value == x)).ToArray();
        visiblePlayers.RemoveWhere(x => Server.GetPlayer(x) == null || !World.Players.Any(p => p.Value == x));

        if (removed.Length > 0)
            await client.QueuePacketAsync(new DestroyEntities(removed));
    }

    private async Task PickupNearbyItemsAsync(float distance = 0.5f)
    {
        foreach (var entity in World.GetEntitiesNear(Position, distance))
        {
            if (entity is ItemEntity item)
            {
                server.BroadcastPacket(new CollectItem
                {
                    CollectedEntityId = item.EntityId,
                    CollectorEntityId = EntityId,
                    PickupItemCount = item.Count
                });

                var slot = Inventory.AddItem(new ItemStack(item.Material, item.Count, item.ItemMeta));

                client.SendPacket(new SetSlot
                {
                    Slot = (short)slot,

                    WindowId = 0,

                    SlotData = Inventory.GetItem(slot),

                    StateId = Inventory.StateId++
                });

                await item.RemoveAsync();
            }
        }
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

    private string GetPlayerDataPath(bool isOld = false) =>
        !isOld ? Path.Join(World.PlayerDataPath, $"{Uuid}.dat") : Path.Join(World.PlayerDataPath, $"{Uuid}.dat.old");
}
