using Obsidian.Serializer.Attributes;
using System.Threading.Tasks;

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

        public PlayerLook(float yaw, float pitch, bool onground) : base(0x12)
        {
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onground;
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.Yaw = await stream.ReadFloatAsync();
            this.Pitch = await stream.ReadFloatAsync();
            this.OnGround = await stream.ReadBooleanAsync();
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteFloatAsync(this.Yaw);
            await stream.WriteFloatAsync(this.Pitch);
            await stream.WriteBooleanAsync(this.OnGround);
        }
    }
}