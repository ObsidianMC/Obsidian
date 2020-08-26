using Obsidian.Serializer.Attributes;
using Obsidian.Util.DataTypes;
using System;
using Obsidian.Serializer.Enums;

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
        [Field(0)]
        public Transform Transform { get; set; }

        [Field(1, Type = DataType.Byte)]
        public PositionFlags Flags { get; private set; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

        [Field(2, Type = DataType.VarInt)]
        public int TeleportId { get; private set; }

        public PlayerPositionLook() : base(0x32) { }

        public PlayerPositionLook(byte[] data) : base(0x32, data) { }

        public PlayerPositionLook(Transform tranform, PositionFlags flags, int tpId) : base(0x32)
        {
            this.Transform = tranform;
            this.Flags = flags;
            this.TeleportId = tpId;
        }
    }
}