using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SpawnLivingEntity : IPacket
    {
        [Field(0), VarLength]
        public int EntityId { get; set; }

        [Field(1)]
        public Guid Uuid { get; set; }

        [Field(2), ActualType(typeof(int)), VarLength]
        public EntityType Type { get; set; }

        [Field(3), Absolute]
        public PositionF Position { get; set; }

        [Field(4)]
        public Angle Yaw { get; set; }

        [Field(5)]
        public Angle Pitch { get; set; }

        [Field(6)]
        public Angle HeadPitch { get; set; }

        [Field(7)]
        public Velocity Velocity { get; set; }

        public int Id => 0x02;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}