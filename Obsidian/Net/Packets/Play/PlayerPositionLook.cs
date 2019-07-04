using Obsidian.Util;
using System;

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

        public PlayerPositionLook(byte[] data) : base(0x32, data)
        {
        }

        [Variable]
        public Transform Transform { get; set; }

        [Variable]
        public PositionFlags Flags { get; private set; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

        [Variable]
        public int TeleportId { get; private set; } = 0;
    }
}