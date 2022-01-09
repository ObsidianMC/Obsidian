﻿// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Microsoft.Extensions.Logging;
using Obsidian.API.Events;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Utilities.Registry;
using System.IO;

namespace Obsidian.Entities;

public class Player : Living, IPlayer
{
    internal readonly Client client;

    internal HashSet<int> visiblePlayers = new();

    //TODO: better name??
    internal short inventorySlot = 36;

    internal bool isDragging;

    private byte containerId = 0;

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
    public string Dimension { get; set; }
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

    internal Player(Guid uuid, string username, Client client)
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

        this.Server = client.Server;
        this.Type = EntityType.Player;
    }

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
        foreach (var (_, player) in this.World.Players.Except(this.Uuid).Where(x => VectorF.Distance(position, x.Value.Position) <= x.Value.client.ClientSettings.ViewDistance))
        {
            if (!this.visiblePlayers.Contains(player.EntityId) && player.Alive)
            {
                this.server.Logger.LogDebug($"Added back: {player.Username}");
                this.visiblePlayers.Add(player.EntityId);

                await this.client.QueuePacketAsync(new SpawnPlayer
                {
                    EntityId = player.EntityId,
                    Uuid = player.Uuid,
                    Position = player.Position,
                    Yaw = player.Yaw,
                    Pitch = player.Pitch
                });
            }
        }

        this.visiblePlayers.RemoveWhere(x => this.Server.GetPlayer(x) == null);
    }

    private async Task PickupNearbyItemsAsync(float distance = 0.5f)
    {
        foreach (var entity in this.World.GetEntitiesNear(this.Position, distance))
        {
            if (entity is ItemEntity item)
            {
                this.server.BroadcastPacket(new CollectItem
                {
                    CollectedEntityId = item.EntityId,
                    CollectorEntityId = this.EntityId,
                    PickupItemCount = item.Count
                });

                var slot = this.Inventory.AddItem(new ItemStack(item.Material, item.Count, item.ItemMeta));

                this.client.SendPacket(new SetSlot
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

        await this.client.QueuePacketAsync(new ScoreboardObjectivePacket
        {
            ObjectiveName = actualBoard.name,
            Mode = ScoreboardMode.Create,
            Value = actualBoard.Objective.Value,
            Type = actualBoard.Objective.DisplayType
        });

        foreach (var (_, score) in actualBoard.scores)
        {
            await this.client.QueuePacketAsync(new UpdateScore
            {
                EntityName = score.DisplayText,
                ObjectiveName = actualBoard.name,
                Action = 0,
                Value = score.Value
            });
        }

        await this.client.QueuePacketAsync(new DisplayScoreboard
        {
            ScoreName = actualBoard.name,
            Position = position
        });
    }

    public async Task OpenInventoryAsync(BaseContainer container)
    {
        this.OpenedContainer = container;

        var nextId = this.GetNextContainerId();

        await this.client.QueuePacketAsync(new OpenWindow(container, nextId));

        if (container.HasItems())
            await this.client.QueuePacketAsync(new WindowItems(nextId, container.ToList()));
    }

    public async Task TeleportAsync(VectorF pos)
    {
        this.LastPosition = this.Position;
        this.Position = pos;
        await this.client.Server.World.ResendBaseChunksAsync(this.client);

        var tid = Globals.Random.Next(0, 999);

        await client.Server.Events.InvokePlayerTeleportedAsync(
            new PlayerTeleportEventArgs
            (
                this,
                this.Position,
                pos
            ));

        await this.client.QueuePacketAsync(new PlayerPositionAndLook
        {
            Position = pos,
            Flags = PositionFlags.None,
            TeleportId = tid
        });
        this.TeleportId = tid;
    }

    public async Task TeleportAsync(IPlayer to)
    {
        this.LastPosition = this.Position;
        this.Position = to.Position;
        await this.client.Server.World.ResendBaseChunksAsync(this.client);
        var tid = Globals.Random.Next(0, 999);
        await this.client.QueuePacketAsync(new PlayerPositionAndLook
        {
            Position = to.Position,
            Flags = PositionFlags.None,
            TeleportId = tid
        });
        this.TeleportId = tid;
    }

    public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null) =>
        this.SendMessageAsync(ChatMessage.Simple(message), type, sender ?? Guid.Empty);

    public Task SendMessageAsync(ChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null) =>
        client.QueuePacketAsync(new ChatMessagePacket(message, type, sender ?? Guid.Empty));

    public Task SendSoundAsync(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
        client.QueuePacketAsync(new SoundEffect(soundId, position, category, volume, pitch));

    public Task SendNamedSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
        client.QueuePacketAsync(new NamedSoundEffect(name, position, category, volume, pitch));

    public Task KickAsync(string reason) => this.client.DisconnectAsync(ChatMessage.Simple(reason));
    public Task KickAsync(ChatMessage reason) => this.client.DisconnectAsync(reason);

    public async Task RespawnAsync()
    {
        if (this.Alive)
            return;

        this.visiblePlayers.Clear();

        Registry.Dimensions.TryGetValue(0, out var codec);

        await this.client.QueuePacketAsync(new Respawn
        {
            Dimension = codec,
            WorldName = "minecraft:world",
            Gamemode = this.Gamemode,
            PreviousGamemode = this.Gamemode,
            HashedSeed = 0,
            IsFlat = false,
            IsDebug = false,
            CopyMetadata = false
        });

        //Gotta send chunks again
        await this.World.ResendBaseChunksAsync(this.client);

        this.Position = this.server.World.Data.SpawnPosition;

        await this.client.QueuePacketAsync(new PlayerPositionAndLook
        {
            Position = this.server.World.Data.SpawnPosition,
            Yaw = 0,
            Pitch = 0,
            Flags = PositionFlags.None,
            TeleportId = 0
        });


        this.Health = 20f;
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

        await this.client.QueuePacketAsync(new ChangeGameState(RespawnReason.EnableRespawnScreen));
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
                    Uuid = this.Uuid,
                    Gamemode = (int)gamemode,
                }
            };

        await this.client.Server.QueueBroadcastPacketAsync(new PlayerInfoPacket(PlayerInfoAction.UpdateGamemode, list));
        await this.client.QueuePacketAsync(new ChangeGameState(gamemode));

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

        await this.client.QueuePacketAsync(titlePacket);
        await this.client.QueuePacketAsync(titleTimesPacket);
    }

    public async Task SendTitleAsync(ChatMessage title, ChatMessage subtitle, int fadeIn, int stay, int fadeOut)
    {
        var titlePacket = new TitlePacket(TitleMode.SetSubtitle)
        {
            Text = subtitle
        };

        await this.client.QueuePacketAsync(titlePacket);

        await this.SendTitleAsync(title, fadeIn, stay, fadeOut);
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

        await this.client.QueuePacketAsync(titlePacket);
        await this.client.QueuePacketAsync(titleTimesPacket);
    }

    public async Task SaveAsync()
    {
        var playerFile = new FileInfo(Path.Join(this.server.ServerFolderPath, this.World.Name, "playerdata", $"{this.Uuid}.dat"));

        await using var playerFileStream = playerFile.Open(FileMode.OpenOrCreate, FileAccess.Write);

        using var writer = new NbtWriter(playerFileStream, NbtCompression.GZip, "");

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

        writer.WriteString("Dimension", this.Dimension);

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

    public async Task LoadAsync()
    {
        var playerFile = new FileInfo(Path.Join(this.server.ServerFolderPath, this.World.Name, "playerdata", $"{this.Uuid}.dat"));

        await this.LoadPermsAsync();

        if (!playerFile.Exists)
        {
            this.Position = this.World.Data.SpawnPosition;
            this.Dimension = "minecraft:overworld";
            return;
        }

        await using var playerFileStream = playerFile.OpenRead();

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

        var dimension = Registry.GetDimensionCodecOrDefault(compound.GetString("Dimension"));

        this.Dimension = dimension != null ? dimension.Name : "minecraft:overworld";

        this.FoodLevel = compound.GetInt("foodLevel");
        this.FoodTickTimer = compound.GetInt("foodTickTimer");
        this.Gamemode = (Gamemode)compound.GetInt("playerGameType");
        this.XpLevel = compound.GetInt("XpLevel");
        this.XpTotal = compound.GetInt("XpTotal");
        this.FallDistance = compound.GetFloat("FallDistance");
        this.FoodExhaustionLevel = compound.GetFloat("foodExhaustionLevel");
        this.FoodSaturationLevel = compound.GetFloat("foodSaturationLevel");
        this.XpP = compound.GetInt("XpP");

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
            this.Position = this.World.Data.SpawnPosition;

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
        var permissions = permissionNode.ToLower().Trim().Split('.');

        var parent = this.PlayerPermissions;

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
}
