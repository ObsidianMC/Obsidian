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

    public class ClientPlayerPositionLook : Packet
    {
        [Field(0, true)]
        public Position Position { get; set; }

        [Field(1)]
        public float Yaw { get; set; }

        [Field(2)]
        public float Pitch { get; set; }

        [Field(3, Type = DataType.Byte)]
        public PositionFlags Flags { get; set; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

        [Field(4, Type = DataType.VarInt)]
        public int TeleportId { get; set; }

        public ClientPlayerPositionLook() : base(0x36) { }

        public ClientPlayerPositionLook(byte[] data) : base(0x36, data) { }

    }
    public class ServerPlayerPositionLook : Packet
    {
        [Field(0, true)]
        public Position Position { get; set; }

        [Field(1)]
        public float Pitch { get; set; }

        [Field(2)]
        public float Yaw { get; set; }

        [Field(3)]
        public bool OnGround { get; set; }

        public ServerPlayerPositionLook() : base(0x11) { }
    }
}