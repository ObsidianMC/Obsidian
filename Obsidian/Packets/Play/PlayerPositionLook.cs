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

    public class PlayerPositionLook : Packet
    {
        public PlayerPositionLook(Location location, PositionFlags flags, int tpId) : base(0x32, new byte[0])
        {
            this.X = location.X;
            this.Y = location.Y;
            this.Z = location.Z;

            this.Yaw = location.Yaw;
            this.Pitch = location.Pitch;

            this.Flags = flags;
            this.TeleportId = tpId;
        }

        public PlayerPositionLook(byte[] data) : base(0x32, data) { }

        public double X { get; private set; } = 0;

        public double Y { get; private set; } = 0;

        public double Z { get; private set; } = 0;

        public float Yaw { get; private set; } = 0;

        public float Pitch { get; private set; } = 0;

        public PositionFlags Flags { get; private set; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

        public int TeleportId { get; private set; } = 0;

        public override async Task Populate()
        {
            using (var stream = new MemoryStream(this._packetData))
            {
                this.X = await stream.ReadDoubleAsync();
                this.Y = await stream.ReadDoubleAsync();
                this.Z = await stream.ReadDoubleAsync();

                this.Pitch = await stream.ReadFloatAsync();
                this.Yaw = await stream.ReadFloatAsync();

                this.Flags = (PositionFlags)await stream.ReadByteAsync();

                this.TeleportId = await stream.ReadVarIntAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MemoryStream())
            {
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
}