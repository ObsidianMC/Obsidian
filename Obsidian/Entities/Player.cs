// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Chat;
using Obsidian.Net;
using Obsidian.Net.Actions.BossBar;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Clientbound.GameState;
using Obsidian.Utilities;
using Obsidian.Utilities.Registry;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class Player : Living, IPlayer
    {
        internal readonly Client client;

        internal HashSet<int> VisiblePlayers = new();

        public bool IsOperator => Server.Operators.IsOperator(this);

        public string Username { get; }

        public string DisplayName { get; internal set; }


        /// <summary>
        /// The players inventory
        /// </summary>
        public Inventory Inventory { get; }
        public Inventory OpenedInventory { get; set; }

        public ItemStack LastClickedItem { get; internal set; }

        public Block LastClickedBlock { get; internal set; }

        public PlayerBitMask PlayerBitMask { get; set; }
        public Gamemode Gamemode { get; set; }

        public Hand MainHand { get; set; } = Hand.MainHand;

        public IScoreboard CurrentScoreboard { get; set; }

        public bool Sleeping { get; set; }
        public bool InHorseInventory { get; set; }
        public bool IsDragging { get; set; }

        public short AttackTime { get; set; }
        public short DeathTime { get; set; }
        public short HurtTime { get; set; }
        public short SleepTimer { get; set; }
        public short CurrentSlot { get; set; } = 36;

        public int Ping => this.client.ping;
        public int Dimension { get; set; }
        public int FoodLevel { get; set; }
        public int FoodTickTimer { get; set; }
        public int XpLevel { get; set; }
        public int XpTotal { get; set; }

        public double HeadY { get; private set; }

        public float AdditionalHearts { get; set; } = 0;
        public float FallDistance { get; set; }
        public float FoodExhastionLevel { get; set; } // not a type, it's in docs like this
        public float FoodSaturationLevel { get; set; }
        public int Score { get; set; } = 0; // idfk, xp points?

        public Entity LeftShoulder { get; set; }
        public Entity RightShoulder { get; set; }

        /* Missing for now:
            NbtCompound(inventory)
            NbtList(Motion)
            NbtList(Pos)
            NbtList(Rotation)
        */

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
            this.Inventory = new Inventory(InventoryType.Generic, 9 * 5 + 1, true)
            {
                Owner = uuid
            };
            this.Server = client.Server;
            this.Type = EntityType.Player;

            LoadPerms();
        }

        internal override async Task UpdateAsync(VectorF position, bool onGround)
        {
            await base.UpdateAsync(position, onGround);

            this.HeadY = position.Y + 1.62f;

            await this.TrySpawnPlayerAsync(position);

            await this.PickupNearbyItemsAsync(1);
        }

        internal override async Task UpdateAsync(VectorF position, Angle yaw, Angle pitch, bool onGround)
        {
            await base.UpdateAsync(position, yaw, pitch, onGround);

            this.HeadY = position.Y + 1.62f;

            await this.TrySpawnPlayerAsync(position);

            await this.PickupNearbyItemsAsync(0.8f);
        }

        internal override async Task UpdateAsync(Angle yaw, Angle pitch, bool onGround)
        {
            await base.UpdateAsync(yaw, pitch, onGround);

            await this.PickupNearbyItemsAsync(2);
        }

        private async Task TrySpawnPlayerAsync(VectorF position)
        {
            foreach (var (_, player) in this.World.Players.Except(this.Uuid).Where(x => VectorF.Distance(position, x.Value.Position) <= 10))//TODO use view distance
            {
                if (!this.VisiblePlayers.Contains(player.EntityId) && player.Alive)
                {
                    this.server.Logger.LogDebug($"Added back: {player.Username}");
                    this.VisiblePlayers.Add(player.EntityId);

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

            this.VisiblePlayers.RemoveWhere(x => this.Server.GetPlayer(x) == null);
        }

        private async Task PickupNearbyItemsAsync(float distance = 0.5f)
        {
            foreach (var entity in this.World.GetEntitiesNear(this.Position, distance))
            {
                if (entity is ItemEntity item)
                {
                    this.server.BroadcastPacketWithoutQueue(new CollectItem
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

                        SlotData = this.Inventory.GetItem(slot)
                    });

                    await item.RemoveAsync();
                }
            }
        }

        internal async Task SendScoreboardInfo(ScoreboardObjectivePacket packet, UpdateScore scorePacket = null)
        {
            await this.client.QueuePacketAsync(packet);

            if (scorePacket != null)
                await this.client.QueuePacketAsync(scorePacket);
        }

        public ItemStack GetHeldItem() => this.Inventory.GetItem(this.CurrentSlot);

        public void LoadPerms()
        {
            // Load a JSON file that contains all permissions
            var server = (Server)this.Server;
            var dir = Path.Combine($"Server-{server.Id}", "permissions");
            var user = server.Config.OnlineMode ? this.Uuid.ToString() : this.Username;
            var file = Path.Combine(dir, $"{user}.json");

            if (File.Exists(file))
                this.PlayerPermissions = JsonConvert.DeserializeObject<Permission>(File.ReadAllText(file));
        }

        public void SavePerms()
        {
            // Save permissions to JSON file
            var server = (Server)this.Server;
            var dir = Path.Combine($"Server-{server.Id}", "permissions");
            var user = server.Config.OnlineMode ? this.Uuid.ToString() : this.Username;
            var file = Path.Combine(dir, $"{user}.json");

            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            if (!File.Exists(file))
                File.Create(file).Close();

            File.WriteAllText(file, JsonConvert.SerializeObject(this.PlayerPermissions, Formatting.Indented));
        }

        public async Task DisplayScoreboardAsync(IScoreboard scoreboard, ScoreboardPosition position)
        {
            var actualBoard = (Scoreboard)scoreboard;

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

        public async Task OpenInventoryAsync(Inventory inventory)
        {
            await this.client.QueuePacketAsync(new OpenWindow(inventory));

            if (inventory.HasItems())
            {
                await this.client.QueuePacketAsync(new WindowItems(inventory.Id, inventory.Items.ToList()));
            }
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

        public async Task TeleportAsync(IPlayer to) => await TeleportAsync(to as Player);
        public async Task TeleportAsync(Player to)
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

        public Task SendMessageAsync(string message, MessageType type = MessageType.Chat, Guid? sender = null) => client.QueuePacketAsync(new ChatMessagePacket(ChatMessage.Simple(message), type, sender ?? Guid.Empty));

        public Task SendMessageAsync(IChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null)
        {
            if (message is not ChatMessage chatMessage)
                return Task.FromException(new Exception("Message was of the wrong type or null. Expected instance supplied by IChatMessage.CreateNew."));

            return this.SendMessageAsync(chatMessage, type, sender);
        }

        public Task SendMessageAsync(ChatMessage message, MessageType type = MessageType.Chat, Guid? sender = null) =>
            client.QueuePacketAsync(new ChatMessagePacket(message, type, sender ?? Guid.Empty));

        public Task SendSoundAsync(Sounds soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
            client.QueuePacketAsync(new SoundEffect(soundId, position, category, volume, pitch));

        public Task SendNamedSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float volume = 1f, float pitch = 1f) =>
            client.QueuePacketAsync(new NamedSoundEffect(name, position, category, volume, pitch));

        public Task SendBossBarAsync(Guid uuid, BossBarAction action) => client.QueuePacketAsync(new Net.Packets.Play.Clientbound.BossBar(uuid, action));

        public Task KickAsync(string reason) => this.client.DisconnectAsync(ChatMessage.Simple(reason));
        public Task KickAsync(IChatMessage reason)
        {
            if (reason is not ChatMessage chatMessage)
                return Task.FromException(new Exception("Message was of the wrong type or null. Expected instance supplied by IChatMessage.CreateNew."));

            return KickAsync(chatMessage);
        }
        public Task KickAsync(ChatMessage reason) => this.client.DisconnectAsync(reason);

        public async Task RespawnAsync()
        {
            this.VisiblePlayers.Clear();

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

            if (!this.Alive)
                this.Health = 20f;
        }

        public override async Task KillAsync(IEntity source, IChatMessage deathMessage)
        {
            await this.client.QueuePacketAsync(new PlayerDied
            {
                PlayerId = this.EntityId,
                EntityId = source != null ? source.EntityId : -1,
                Message = deathMessage as ChatMessage
            });

            await this.client.QueuePacketAsync(new EnableRespawnScreen(RespawnReason.EnableRespawnScreen));
            await this.RemoveAsync();

            if (source is Player attacker)
                attacker.VisiblePlayers.Remove(this.EntityId);
        }

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await base.WriteAsync(stream);

            await stream.WriteEntityMetdata(14, EntityMetadataType.Float, this.AdditionalHearts);

            await stream.WriteEntityMetdata(15, EntityMetadataType.VarInt, this.Score);

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
            stream.WriteVarInt(Score);

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

        public override string ToString() => this.Username;

        public async Task SetGamemodeAsync(Gamemode gamemode)
        {
            var list = new List<PlayerInfoAction>()
            {
                new PlayerInfoUpdateGamemodeAction()
                {
                    Uuid = this.Uuid,
                    Gamemode = (int)gamemode,
                }
            };

            await this.client.Server.BroadcastPacketAsync(new PlayerInfo(1, list));
            await this.client.QueuePacketAsync(new ChangeGameState(gamemode));

            this.Gamemode = gamemode;
        }

        public async Task UpdateDisplayNameAsync(string newDisplayName)
        {
            var list = new List<PlayerInfoAction>()
            {
                new PlayerInfoUpdateDisplayNameAction()
                {
                    Uuid = this.Uuid,
                    DisplayName = newDisplayName,
                }
            };

            await this.client.Server.BroadcastPacketAsync(new PlayerInfo(3, list));

            this.DisplayName = newDisplayName;
        }

        public async Task<bool> GrantPermission(string permission)
        {
            // trim and split permission string
            permission = permission.ToLower().Trim();
            string[] split = permission.Split('.');

            // Set root node and whether we created a new permission (still false)
            var parent = this.PlayerPermissions;
            var result = false;

            foreach (var i in split)
            {
                // no such child, this permission is new!
                if (!parent.Children.Any(x => x.Name == i))
                {
                    // create the new child, add it to its parent and set parent to the next value to continue the loop
                    var child = new Permission(i);
                    parent.Children.Add(child);
                    parent = child;
                    // yes, new permission!
                    result = true;
                    continue;
                }

                // child already exists, set parent to existing child to continue loop
                parent = parent.Children.First(x => x.Name == i);
            }

            this.SavePerms();

            if (result)
                await this.client.Server.Events.InvokePermissionGrantedAsync(new PermissionGrantedEventArgs(this, permission));
            return result;
        }

        public async Task<bool> RevokePermission(string permission)
        {
            // trim and split permission string

            permission = permission.ToLower().Trim();
            string[] split = permission.Split('.');

            // Set root node and whether we created a new permission (still false)
            var parent = this.PlayerPermissions;
            var childToRemove = this.PlayerPermissions;
            var result = true;

            foreach (var i in split)
            {
                if (parent.Children.Any(x => x.Name == i))
                {
                    // child exists, set its parent node and mark it to be removed
                    parent = childToRemove;
                    childToRemove = parent.Children.First(x => x.Name == i);
                    continue;
                }
                // no such child node, result false and break
                result = false;
                break;
            }

            if (result)
            {
                parent.Children.Remove(childToRemove);
                this.SavePerms();
                await this.client.Server.Events.InvokePermissionRevokedAsync(new PermissionRevokedEventArgs(this, permission));
            }
            return result;
        }

        public Task<bool> HasPermission(string permission)
        {
            // trim and split permission string
            permission = permission.ToLower().Trim();
            string[] split = permission.Split('.');


            // Set root node and whether we created a new permission (still false)
            var result = false;
            var parent = this.PlayerPermissions;


            foreach (var i in split)
            {
                if (parent.Children.Any(x => x.Name == "*"))
                {
                    // WILDCARD! all child permissions are granted here.
                    result = true;
                    break;
                }
                if (parent.Children.Any(x => x.Name == i))
                {
                    parent = parent.Children.First(x => x.Name == i);
                    result = true;
                }
                else
                {
                    // no such child. break loop and stop searching.
                    result = false;
                    break;
                }
            }

            return Task.FromResult(result);
        }

        public async Task<bool> HasAnyPermission(IEnumerable<string> permissions)
        {
            foreach (var perm in permissions)
            {
                if (await HasPermission(perm)) return true;
            }
            return false;
        }

        public async Task<bool> HasAllPermissions(IEnumerable<string> permissions)
        {
            foreach (var perm in permissions)
            {
                if (!await HasPermission(perm)) return false;
            }
            return true;
        }
    }
}