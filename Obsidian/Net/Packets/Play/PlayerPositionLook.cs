using System;
using System.Threading.Tasks;
using Obsidian.Util.DataTypes;

namespace Obsidian.Net.Packets.Play
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
        public PlayerPositionLook(Transform tranform, PositionFlags flags, int tpId) : base(0x32, Array.Empty<byte>())
        {
            this.Transform = tranform;

            this.Flags = flags;
            this.TeleportId = tpId;
        }

        public PlayerPositionLook(byte[] data) : base(0x32, data)
        {
        }

        public Transform Transform { get; set; }

        public PositionFlags Flags { get; private set; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

        public int TeleportId { get; private set; }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteDoubleAsync(this.Transform.X);
            await stream.WriteDoubleAsync(this.Transform.Y);
            await stream.WriteDoubleAsync(this.Transform.Z);
            await stream.WriteFloatAsync(this.Transform.Yaw.Degrees);
            await stream.WriteFloatAsync(this.Transform.Pitch.Degrees);
            await stream.WriteByteAsync((sbyte)this.Flags);
            await stream.WriteVarIntAsync(this.TeleportId);
        }

        protected override async Task PopulateAsync(MinecraftStream stream)
        {
            this.Transform = new Transform(await stream.ReadDoubleAsync(), await stream.ReadDoubleAsync(), await stream.ReadDoubleAsync(), await stream.ReadFloatAsync(), await stream.ReadFloatAsync());

            this.Flags = (PositionFlags)await stream.ReadByteAsync();

            this.TeleportId = await stream.ReadVarIntAsync();
        }
    }
}