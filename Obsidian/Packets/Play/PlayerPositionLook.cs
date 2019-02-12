using Obsidian.Entities;
using Obsidian.Packets.Handshaking;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Packets
{
    [Flags]
    public enum PositionFlags : sbyte
    {
        X = 0x01,
        Y = 0x02,
        Z = 0x04,
        Y_ROT = 0x08,
        X_ROT = 0x10,
        NONE = 0x00
    }

    public class PlayerPositionLook
    {
        public PlayerPositionLook(double x, double y, double z, float yaw, float pitch, PositionFlags flags, int teleportid)
        {
            this.X = x;
            this.Y = y;
            this.Z = z;
            this.Yaw = yaw;
            this.Pitch = pitch;
            this.Flags = flags;
            this.TeleportId = teleportid;
        }

        public double X { get; private set; } = 0;

        public double Y { get; private set; } = 0;

        public double Z { get; private set; } = 0;

        public float Yaw { get; private set; } = 0;

        public float Pitch { get; private set; } = 0;

        public PositionFlags Flags { get; private set; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

        public int TeleportId { get; private set; } = 0;

        public static async Task<PlayerPositionLook> FromArrayAsync(byte[] data)
        {
            MemoryStream stream = new MemoryStream(data);
            return new PlayerPositionLook(await stream.ReadDoubleAsync(), await stream.ReadDoubleAsync(),
                await stream.ReadDoubleAsync(), await stream.ReadFloatAsync(), await stream.ReadFloatAsync(),
                (PositionFlags)await stream.ReadByteAsync(), await stream.ReadVarIntAsync());
        }

        public async Task<byte[]> ToArrayAsync()
        {
            MemoryStream stream = new MemoryStream();
            await stream.WriteDoubleAsync(this.X);
            await stream.WriteDoubleAsync(this.Y);
            await stream.WriteDoubleAsync(this.Z);
            await stream.WriteFloatAsync(this.Yaw);
            await stream.WriteFloatAsync(this.Pitch);
            await stream.WriteByteAsync((sbyte)this.Flags);
            await stream.WriteVarIntAsync(this.TeleportId);
            return stream.ToArray();
        }
    }
}