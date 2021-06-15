using Obsidian.API;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class Particle : IClientboundPacket
    {
        [Field(0), ActualType(typeof(int))]
        public ParticleType Type { get; set; }

        /// <summary>
        /// If true, particle distance increases from 256 to 65536.
        /// </summary>
        [Field(1)]
        public bool LongDistance { get; set; }

        [Field(2), DataFormat(typeof(double))]
        public VectorF Position { get; set; }

        [Field(3), DataFormat(typeof(float))]
        public VectorF Offset { get; set; }

        [Field(6)]
        public float ParticleData { get; set; }

        [Field(7)]
        public int ParticleCount { get; set; }

        [Field(8)]
        public ParticleData Data { get; set; }

        public int Id => 0x22;

        public Particle(ParticleType type, VectorF position, int particleCount)
        {
            Type = type;
            Position = position;
            ParticleCount = particleCount;
        }
    }
}
