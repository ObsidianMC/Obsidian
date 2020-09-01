using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
{
    public class PlayerLook : Packet
    {
        [Field(0)]
        public float Yaw { get; private set; }

        [Field(1)]
        public float Pitch { get; private set; }

        [Field(2)]
        public bool OnGround { get; private set; }

        public PlayerLook() : base(0x12) { }

        public PlayerLook(byte[] data) : base(0x12, data) { }

        public PlayerLook(Angle yaw, Angle pitch, bool onground) : base(0x12)
        {
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onground;
        }
    }
}