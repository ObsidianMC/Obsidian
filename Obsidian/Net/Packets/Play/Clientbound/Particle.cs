using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class Particle : ISerializablePacket
    {
        [Field(0), ActualType(typeof(int))]
        public ParticleType Type { get; set; }

        /// <summary>
        /// If true, particle distance increases from 256 to 65536.
        /// </summary>
        [Field(1)]
        public bool LongDistance { get; set; }

        [Field(2), Absolute]
        public PositionF Position { get; set; }

        [Field(3)]
        public float OffsetX { get; set; }

        [Field(4)]
        public float OffsetY { get; set; }

        [Field(5)]
        public float OffsetZ { get; set; }

        [Field(6)]
        public float ParticleData { get; set; }

        [Field(7)]
        public int ParticleCount { get; set; }

        [Field(8)]
        public ParticleData Data { get; set; }

        public int Id => 0x22;

        public Particle(ParticleType type, PositionF position, int particleCount)
        {
            Type = type;
            Position = position;
            ParticleCount = particleCount;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}
