using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    public class PlayerLook
    {
        public PlayerLook(float yaw, float pitch, bool onground)
        {
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.OnGround = onground;
        }

        public float Yaw { get; private set; } = 0;

        public float Pitch { get; private set; } = 0;

        public bool OnGround { get; private set; } = false;

        public static async Task<PlayerLook> FromArrayAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            return new PlayerLook(await stream.ReadFloatAsync(), await stream.ReadFloatAsync(), await stream.ReadBooleanAsync());
        }

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteFloatAsync(this.Yaw);
            await stream.WriteFloatAsync(this.Pitch);
            await stream.WriteBooleanAsync(this.OnGround);
            return stream.ToArray();
        }
    }
}