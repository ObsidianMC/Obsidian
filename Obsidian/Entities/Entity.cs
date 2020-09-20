using Obsidian.Chat;
using Obsidian.Net;
using Obsidian.Util.DataTypes;
using System;
using System.Threading.Tasks;
using System.Timers;

namespace Obsidian.Entities
{
    public class Entity
    {
        public readonly Timer TickTimer = new Timer();

        #region Location properties
        internal Position LastPosition { get; set; } = new Position();

        public Position Position { get; set; } = new Position();        
        #endregion Location properties

        public int EntityId { get; internal set; }

        public EntityBitMask EntityBitMask { get; set; }

        public Pose Pose { get; set; } = Pose.Standing;

        public int Air { get; set; } = 300;

        public ChatMessage CustomName { get; private set; }

        public bool CustomNameVisible { get; private set; }
        public bool Silent { get; private set; }
        public bool NoGravity { get; set; }

        public Entity() { }

        public virtual async Task WriteAsync(MinecraftStream stream)
        {
            await stream.WriteEntityMetdata(0, EntityMetadataType.Byte, (byte)this.EntityBitMask);

            await stream.WriteEntityMetdata(1, EntityMetadataType.VarInt, this.Air);

            await stream.WriteEntityMetdata(2, EntityMetadataType.OptChat, this.CustomName, this.CustomName != null);

            await stream.WriteEntityMetdata(3, EntityMetadataType.Boolean, this.CustomNameVisible);
            await stream.WriteEntityMetdata(4, EntityMetadataType.Boolean, this.Silent);
            await stream.WriteEntityMetdata(5, EntityMetadataType.Boolean, this.NoGravity);
            await stream.WriteEntityMetdata(6, EntityMetadataType.Pose, this.Pose);
        }

    }

    public enum Pose
    {
        Standing,

        FallFlying,

        Sleeping,

        Swimming,

        SpinAttack,

        Sneaking,

        Dying
    }

    [Flags]
    public enum EntityBitMask : byte
    {
        None = 0x00,
        OnFire = 0x01,
        Crouched = 0x02,

        [Obsolete]
        Riding = 0x04,

        Sprinting = 0x08,
        Swimming = 0x10,
        Invisible = 0x20,
        Glowing = 0x40,
        Flying = 0x80
    }
}