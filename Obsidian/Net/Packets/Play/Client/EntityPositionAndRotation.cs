using Obsidian.Serializer.Attributes;
using Obsidian.API;
using Obsidian.Entities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public class EntityPositionAndRotation : IPacket
    {
        [Field(0, Type = Serializer.Enums.DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public short DeltaX { get; set; }

        [Field(2)]
        public short DeltaY { get; set; }

        [Field(3)]
        public short DeltaZ { get; set; }

        [Field(4)]
        public Angle Yaw { get; set; }

        [Field(5)]
        public Angle Pitch { get; set; }

        [Field(6)]
        public bool OnGround { get; set; }

        public int Id => 0x28;

        public EntityPositionAndRotation() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
