using Obsidian.Entities;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.API;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class SpawnEntity : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2, Type = DataType.VarInt)]
        public EntityType Type { get; set; }

        [Field(3, true)]
        public Position Position { get; set; }

        [Field(4)]
        public Angle Pitch { get; set; }

        [Field(5)]
        public Angle Yaw { get; set; }

        [Field(6)]
        public int Data { get; set; }

        [Field(7)]
        public Velocity Velocity { get; set; }

        public int Id => 0x00;

        public SpawnEntity() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}
