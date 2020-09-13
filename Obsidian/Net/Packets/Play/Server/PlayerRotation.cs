using Obsidian.Serializer.Attributes;

namespace Obsidian.Net.Packets.Play.Server
{
    public class PlayerRotation : Packet
    {
        [Field(0)]
        public float Yaw { get => this.yaw; set => this.yaw = (value % 360 + 360) % 360; }

        private float yaw;

        [Field(1)]
        public float Pitch { get; set; }

        [Field(2)]
        public bool OnGround { get; set; }

        public PlayerRotation() : base(0x12) { }

        public PlayerRotation(byte[] data) : base(0x12, data) { }

        public PlayerRotation(float yaw, float pitch, bool onground) : base(0x12)
        {
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onground;
        }
    }
}