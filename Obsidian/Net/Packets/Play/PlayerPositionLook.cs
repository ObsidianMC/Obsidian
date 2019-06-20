using Obsidian.Entities;
using Obsidian.Util;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
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
        public PlayerPositionLook(Transform tranform, PositionFlags flags, int tpId) : base(0x32, new byte[0])
        {
            this.Transform = tranform;

            this.Flags = flags;
            this.TeleportId = tpId;
        }

        public PlayerPositionLook(byte[] data) : base(0x32, data) { }

        public Transform Transform { get; set; }

        public PositionFlags Flags { get; private set; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

        public int TeleportId { get; private set; } = 0;

        public override async Task PopulateAsync()
        {
            using (var stream = new MinecraftStream(this.PacketData))
            {
                this.Transform = new Transform(await stream.ReadDoubleAsync(), await stream.ReadDoubleAsync(), await stream.ReadDoubleAsync(), await stream.ReadFloatAsync(), await stream.ReadFloatAsync());

                this.Flags = (PositionFlags)await stream.ReadByteAsync();

                this.TeleportId = await stream.ReadVarIntAsync();
            }
        }

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteDoubleAsync(this.Transform.X);
                await stream.WriteDoubleAsync(this.Transform.Y);
                await stream.WriteDoubleAsync(this.Transform.Z);
                await stream.WriteFloatAsync(this.Transform.Yaw);
                await stream.WriteFloatAsync(this.Transform.Pitch);
                await stream.WriteByteAsync((sbyte)this.Flags);
                await stream.WriteVarIntAsync(this.TeleportId);
                return stream.ToArray();
            }
        }
    }
}