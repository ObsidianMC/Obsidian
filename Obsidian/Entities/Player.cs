// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Obsidian.Boss;
using Obsidian.Chat;
using Obsidian.Concurrency;
using Obsidian.Items;
using Obsidian.Net;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Client;
using Obsidian.PlayerData;
using Obsidian.Sounds;
using Obsidian.Util.DataTypes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Entities
{
    public class Player : Living
    {
        internal readonly Client client;

        #region Location properties
        internal Position LastPosition { get; set; } = new Position();

        internal Angle LastPitch { get; set; }

        internal Angle LastYaw { get; set; }


        public Position Position { get; set; } = new Position();

        public Angle Pitch { get; set; }

        public Angle Yaw { get; set; }
        #endregion Location properties

        public string Username { get; }

        /// <summary>
        /// The players inventory
        /// </summary>
        public Inventory Inventory { get; private set; } = new Inventory();
        public Inventory OpenedInventory { get; set; }

        public Guid Uuid { get; set; }

        public PlayerBitMask PlayerBitMask { get; set; }
        public Gamemode Gamemode { get; set; }
        public Hand MainHand { get; set; } = Hand.MainHand;

        public bool OnGround { get; set; }
        public bool Sleeping { get; set; }

        public short AttackTime { get; set; }
        public short DeathTime { get; set; }
        public short HurtTime { get; set; }
        public short SleepTimer { get; set; }
        public short CurrentSlot { get; set; }

        public int Ping => this.client.ping;
        public int Dimension { get; set; }
        public int FoodLevel { get; set; }
        public int FoodTickTimer { get; set; }
        public int XpLevel { get; set; }
        public int XpTotal { get; set; }

        public float AdditionalHearts { get; set; } = 0;
        public float FallDistance { get; set; }
        public float FoodExhastionLevel { get; set; } // not a type, it's in docs like this
        public float FoodSaturationLevel { get; set; }
        public float XpP { get; set; } = 0; // idfk, xp points?

        public Entity LeftShoulder { get; set; }
        public Entity RightShoulder { get; set; }

        public World World { get; }

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
        public ConcurrentHashSet<string> Permissions { get; } = new ConcurrentHashSet<string>();

        internal Player(Guid uuid, string username, Client client)
        {
            this.Uuid = uuid;
            this.Username = username;
            this.client = client;
            this.EntityId = client.id;
        }

        internal async Task UpdateAsync(Position position, bool onGround)
        {
            short newX = (short)((position.X * 32 - this.LastPosition.X * 32) * 64);
            short newY = (short)((position.Y * 32 - this.LastPosition.Y * 32) * 64);
            short newZ = (short)((position.Z * 32 - this.LastPosition.Z * 32) * 64);

            var isNewLocation = position != this.LastPosition;

            if (isNewLocation)
            {
                await this.client.Server.BroadcastPacketAsync(new EntityPosition
                {
                    EntityId = this.client.id,

                    DeltaX = newX,
                    DeltaY = newY,
                    DeltaZ = newZ,

                    OnGround = onGround
                }, this);
                this.UpdatePosition(position, onGround);
            }
        }

        internal async Task UpdateAsync(Angle yaw, Angle pitch, bool onGround)
        {
            var isNewRotation = yaw.Value != this.LastYaw.Value || pitch.Value != this.LastPitch.Value;

            if (isNewRotation)
            {
                await this.client.Server.BroadcastPacketAsync(new EntityRotation
                {
                    EntityId = this.client.id,
                    OnGround = onGround,
                    Yaw = yaw,
                    Pitch = pitch
                }, this);

                this.UpdatePosition(yaw, pitch, onGround);
            }
        }

        //TODO move all location and rotation properties to the LivingEntity class
        internal async Task UpdateAsync(Position position, Angle yaw, Angle pitch, bool onGround)
        {
            short newX = (short)((position.X * 32 - this.LastPosition.X * 32) * 64);
            short newY = (short)((position.Y * 32 - this.LastPosition.Y * 32) * 64);
            short newZ = (short)((position.Z * 32 - this.LastPosition.Z * 32) * 64);

            var isNewLocation = position != this.LastPosition;

            var isNewRotation = yaw.Value != this.LastYaw.Value || pitch.Value != this.LastPitch.Value;

            if (isNewRotation)
                this.CopyLook();

            if (isNewLocation)
            {
                if (isNewRotation)
                {
                    await this.client.Server.BroadcastPacketAsync(new EntityPositionAndRotation
                    {
                        EntityId = this.client.id,

                        DeltaX = newX,
                        DeltaY = newY,
                        DeltaZ = newZ,

                        Yaw = yaw,

                        Pitch = pitch,

                        OnGround = onGround
                    }, this);
                }
                else
                {
                    await this.client.Server.BroadcastPacketAsync(new EntityPosition
                    {
                        EntityId = this.client.id,

                        DeltaX = newX,
                        DeltaY = newY,
                        DeltaZ = newZ,

                        OnGround = onGround
                    }, this);
                }

                this.UpdatePosition(position, yaw, pitch, onGround);
            }
        }

        public void UpdatePosition(Position pos, bool onGround = true)
        {
            this.CopyPosition();
            this.Position = pos;
            this.OnGround = onGround;
        }

        public void UpdatePosition(Position pos, Angle yaw, Angle pitch, bool onGround = true)
        {
            this.CopyPosition(true);
            this.Position = pos;
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onGround;
        }

        public void UpdatePosition(double x, double y, double z, bool onGround = true)
        {
            this.CopyPosition();
            this.Position.X = x;
            this.Position.Y = y;
            this.Position.Z = z;
            this.OnGround = onGround;
        }

        public void UpdatePosition(Angle yaw, Angle pitch, bool onGround = true)
        {
            this.CopyLook();
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onGround;
        }

        public void LoadPerms(List<string> permissions)
        {
            foreach (var perm in permissions)
            {
                Permissions.Add(perm);
            }
        }

        public Task SendMessageAsync(string message, sbyte position = 0) => client.QueuePacketAsync(new ChatMessagePacket(ChatMessage.Simple(message), position));

        public Task SendMessageAsync(ChatMessage message) => client.QueuePacketAsync(new ChatMessagePacket(message, 0));

        public Task SendSoundAsync(int soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f) => client.QueuePacketAsync(new SoundEffect(soundId, position, category, pitch, volume));

        public Task SendNamedSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f) => client.QueuePacketAsync(new NamedSoundEffect(name, position, category, pitch, volume));

        public Task SendBossBarAsync(Guid uuid, BossBarAction action) => client.QueuePacketAsync(new BossBar(uuid, action));

        public Task KickAsync(string reason) => this.client.DisconnectAsync(ChatMessage.Simple(reason));
        public Task KickAsync(ChatMessage reason) => this.client.DisconnectAsync(reason);
        public override async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteEntityMetdata(11, EntityMetadataType.Float, AdditionalHearts);

            await stream.WriteEntityMetdata(12, EntityMetadataType.VarInt, XpP);

            await stream.WriteEntityMetdata(13, EntityMetadataType.Byte, (int)PlayerBitMask);

            await stream.WriteEntityMetdata(14, EntityMetadataType.Byte, (byte)1);
        }

        internal void CopyPosition(bool withLook = false)
        {

            this.LastPosition = this.Position;

            if (withLook)
                this.CopyLook();
        }

        internal void CopyLook()
        {
            this.LastYaw = this.Yaw;
            this.LastPitch = this.Pitch;
        }
    }

    [Flags]
    public enum PlayerBitMask : byte
    {
        Unused = 0x80,

        CapeEnabled = 0x01,
        JacketEnabled = 0x02,

        LeftSleeveEnabled = 0x04,
        RightSleeveEnabled = 0x08,

        LeftPantsLegEnabled = 0x10,
        RIghtPantsLegEnabled = 0x20,

        HatEnabled = 0x40
    }
}