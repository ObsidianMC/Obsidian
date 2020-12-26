using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SpawnPlayer : IPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2), Absolute]
        public PositionF Position { get; set; }

        [Field(3)]
        public Angle Yaw { get; set; }

        [Field(4)]
        public Angle Pitch { get; set; }

        public int Id => 0x04;

        public SpawnPlayer()
        {
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}