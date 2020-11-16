using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using Obsidian.API;
using System;
using Obsidian.Entities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Client
{
    public partial class SpawnPlayer : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2, true)]
        public Position Position { get; set; }

        [Field(3)]
        public Angle Yaw { get; set; }

        [Field(4)]
        public Angle Pitch { get; set; }

        public int Id => 0x04;

        public SpawnPlayer() { }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}