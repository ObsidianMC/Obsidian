using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [Flags]
    public enum PositionFlags : sbyte
    {
        X = 0x01,
        Y = 0x02,
        Z = 0x04,
        RotationY = 0x08,
        RotationX = 0x10,
        None = 0x00
    }

    [ClientOnly]
    public partial class ClientPlayerPositionLook : ISerializablePacket
    {
        [Field(0), Absolute]
        public PositionF Position { get; set; }

        [Field(1)]
        public float Yaw { get; set; }

        [Field(2)]
        public float Pitch { get; set; }

        [Field(3), ActualType(typeof(sbyte))]
        public PositionFlags Flags { get; set; } = PositionFlags.X | PositionFlags.Y | PositionFlags.Z;

        [Field(4), VarLength]
        public int TeleportId { get; set; }

        public int Id => 0x34;

        public ClientPlayerPositionLook()
        {
        }

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}