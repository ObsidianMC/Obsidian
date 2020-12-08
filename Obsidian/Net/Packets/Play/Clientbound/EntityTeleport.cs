using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class EntityTeleport : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1, true)]
        public Position Position { get; set; }

        [Field(2)]
        public Angle Yaw { get; set; }

        [Field(3)]
        public Angle Pitch { get; set; }

        [Field(4)]
        public bool OnGround { get; set; }

        public int Id => 0x56;

        public EntityTeleport() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
