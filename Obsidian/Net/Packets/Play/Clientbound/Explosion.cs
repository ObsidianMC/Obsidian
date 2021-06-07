using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class Explosion : IClientboundPacket
    {
        [Field(0)]
        public float X;

        [Field(1)]
        public float Y;

        [Field(2)]
        public float Z;

        [Field(3)]
        public float Strength;

        [Field(4)]
        public ExplosionRecord[] Records;

        [Field(5)]
        public float PlayerMotionX;

        [Field(6)]
        public float PlayerMotionY;

        [Field(7)]
        public float PlayerMotionZ;

        public int Id => 0x1B;
    }

    public struct ExplosionRecord
    {
        public sbyte X;
        public sbyte Y;
        public sbyte Z;
    }
}
