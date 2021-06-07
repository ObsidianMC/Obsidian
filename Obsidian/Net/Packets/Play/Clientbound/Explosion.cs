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

        /// <summary>
        /// array of X, Y, Z (separate bytes) for destroyed blocks!
        /// </summary>
        [Field(4)]
        public sbyte[] Records;

        [Field(5)]
        public float PlayerMotionX;

        [Field(6)]
        public float PlayerMotionY;

        [Field(7)]
        public float PlayerMotionZ;

        public int Id => 0x1B;
    }
}
