// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Obsidian.Boss;
using Obsidian.Chat;
using Obsidian.Concurrency;
using Obsidian.Net;
using Obsidian.Net.Packets.Play;
using Obsidian.PlayerData;
using Obsidian.Sounds;
using Obsidian.Util.DataTypes;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Hand = Obsidian.PlayerData.Hand;

namespace Obsidian.Entities
{
    public class Player : Living
    {
        internal readonly Client client;

        public Guid Uuid { get; set; }

        public Transform PreviousTransform { get; set; } = new Transform();

        // Properties set by Minecraft (official)
        public Transform Transform { get; set; }

        public PlayerBitMask PlayerBitMask { get; set; }

        public bool OnGround { get; set; }
        public bool Sleeping { get; set; }

        public short AttackTime { get; set; }
        public short DeathTime { get; set; }
        public short HurtTime { get; set; }
        public short SleepTimer { get; set; }
        public short HeldItemSlot { get; set; }

        public Gamemode Gamemode { get; set; }

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
        
        public Hand MainHand { get; set; }

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
        public ConcurrentHashSet<string> Permissions { get; }

        public string Username { get; }

        public World.World World;

        internal Player(Guid uuid, string username, Client client)
        {
            this.Uuid = uuid;
            this.Username = username;
            this.Permissions = new ConcurrentHashSet<string>();
            this.Transform = new Transform();
            this.client = client;
        }

        public void UpdatePosition(Position pos, bool? onGround = null)
        {
            CopyTransform();
            this.Transform.X = pos.X;
            this.Transform.Y = pos.Y;
            this.Transform.Z = pos.Z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(Transform pos, bool? onGround = null)
        {
            CopyTransform();
            this.Transform.X = pos.X;
            this.Transform.Y = pos.Y;
            this.Transform.Z = pos.Z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(double x, double y, double z, bool? onGround = null)
        {
            CopyTransform();
            this.Transform.X = x;
            this.Transform.Y = y;
            this.Transform.Z = z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(float pitch, float yaw, bool? onGround = null)
        {
            CopyTransform();
            this.Transform.Pitch = pitch;
            this.Transform.Yaw = yaw;
            this.OnGround = onGround ?? this.OnGround;
        }

        private void CopyTransform()
        {
            this.PreviousTransform.X = this.Transform.X;
            this.PreviousTransform.Y = this.Transform.Y;
            this.PreviousTransform.Z = this.Transform.Z;
            this.PreviousTransform.Yaw = this.Transform.Yaw;
            this.PreviousTransform.Pitch = this.Transform.Pitch;
            this.PreviousTransform.ToPosition = this.Transform.ToPosition;
        }

        public Task SendMessageAsync(string message, sbyte position = 0) => client.SendPacketAsync(new ChatMessagePacket(ChatMessage.Simple(message), position));

        public Task SendMessageAsync(ChatMessage message) => client.SendPacketAsync(new ChatMessagePacket(message, 0));

        public Task SendSoundAsync(int soundId, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f) => client.SendPacketAsync(new SoundEffect(soundId, position, category, pitch, volume));

        public Task SendNamedSoundAsync(string name, SoundPosition position, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f) => client.SendPacketAsync(new NamedSoundEffect(name, position, category, pitch, volume));

        public Task SendBossBarAsync(Guid uuid, BossBarAction action) => client.SendPacketAsync(new BossBar(uuid, action));

        public Task KickAsync(string reason) => this.client.DisconnectAsync(ChatMessage.Simple(reason));

        public void LoadPerms(List<string> permissions)
        {
            foreach (var perm in permissions)
            {
                Permissions.Add(perm);
            }
        }

        public Task DisconnectAsync(ChatMessage reason) => this.client.DisconnectAsync(reason);

        public override async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteEntityMetdata(11, EntityMetadataType.Float, AdditionalHearts);

            await stream.WriteEntityMetdata(12, EntityMetadataType.VarInt, XpP);

            await stream.WriteEntityMetdata(13, EntityMetadataType.Byte, (int)PlayerBitMask);

            await stream.WriteEntityMetdata(14, EntityMetadataType.Byte, (byte)1);
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