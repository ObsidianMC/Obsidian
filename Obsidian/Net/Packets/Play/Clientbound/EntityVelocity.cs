using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class EntityVelocity : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public Velocity Velocity { get; set; }

        public int Id => 0x46;

        public EntityVelocity() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
