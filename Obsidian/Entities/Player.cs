// This would be saved in a file called [playeruuid].dat which holds a bunch of NBT data.
// https://wiki.vg/Map_Format
using Obsidian.Boss;
using Obsidian.Chat;
using Obsidian.Concurrency;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.PlayerData;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Obsidian.Net.Packets.Play;
using Obsidian.Sounds;
using Obsidian.Util.DataTypes;
using Hand = Obsidian.PlayerData.Hand;

namespace Obsidian.Entities
{
    public class Player : Living
    {
        private readonly Client Client;

        public Guid Uuid { get; set; }

        public string Uuid3 { get; }

        public Util.DataTypes.Transform PreviousTransform { get; set; }

        // Properties set by Minecraft (official)
        public Util.DataTypes.Transform Transform
        {
            get => _transform;
            set { PreviousTransform = _transform; _transform = value; }
        } // making sure PreviousTransform gets set on update, for comparison in world class.

        private Util.DataTypes.Transform _transform;

        public PlayerBitMask PlayerBitMask { get; set; }

        public bool OnGround { get; set; }
        public bool Sleeping { get; set; }

        public short AttackTime { get; set; }
        public short DeathTime { get; set; }
        public short HurtTime { get; set; }
        public short SleepTimer { get; set; }

        public Gamemode Gamemode { get; set; }

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

        /// <summary>
        /// This is a temporary solution will be removed when I am able to think of a workaround.
        /// </summary>
        public bool Connected => this.Client.Tcp.Connected;

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
            this.Transform = new Util.DataTypes.Transform();
            this.Client = client;

            this.Uuid3 = $"OfflinePlayer:{username}";
        }

        public void UpdatePosition(Position pos, bool? onGround = null)
        {
            this.Transform.X = pos.X;
            this.Transform.Y = pos.Y;
            this.Transform.Z = pos.Z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(Util.DataTypes.Transform pos, bool? onGround = null)
        {
            this.Transform.X = pos.X;
            this.Transform.Y = pos.Y;
            this.Transform.Z = pos.Z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(double x, double y, double z, bool? onGround = null)
        {
            this.Transform.X = x;
            this.Transform.Y = y;
            this.Transform.Z = z;
            this.OnGround = onGround ?? this.OnGround;
        }

        public void UpdatePosition(float pitch, float yaw, bool? onGround = null)
        {
            this.Transform.Pitch = pitch;
            this.Transform.Yaw = yaw;
            this.OnGround = onGround ?? this.OnGround;
        }

        public async Task SendMessageAsync(string message, byte position = 0)
        {
            var chat = ChatMessage.Simple(message);
            await new ChatMessagePacket(chat, position).WriteAsync(this.Client.MinecraftStream);
        }

        public async Task SendMessageAsync(ChatMessage message)
        {
            await new ChatMessagePacket(message, 0).WriteAsync(this.Client.MinecraftStream);
        }

        public async Task SendSoundAsync(int soundId, Position location, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f)
        {
            await new SoundEffect(soundId, location, category, pitch, volume).WriteAsync(this.Client.MinecraftStream);
        }

        public async Task SendNamedSoundAsync(string name, Position location, SoundCategory category = SoundCategory.Master, float pitch = 1f, float volume = 1f)
        {
            await new NamedSoundEffect(name, location, category, pitch, volume).WriteAsync(this.Client.MinecraftStream);
        }

        public async Task SendBossBarAsync(Guid uuid, BossBarAction action)
        {
            await new BossBar(uuid, action).WriteAsync(this.Client.MinecraftStream);
        }

        public async Task KickAsync(string reason)
        {
            await this.Client.DisconnectAsync(ChatMessage.Simple(reason));
        }

        public void LoadPerms(List<string> permissions)
        {
            foreach (var perm in permissions)
            {
                Permissions.Add(perm);
            }
        }

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