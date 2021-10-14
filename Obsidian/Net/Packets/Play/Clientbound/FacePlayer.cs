using Obsidian.Serialization.Attributes;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class FacePlayer : IPacket
    {
        public int Id => 0x37;

        [Field(0), VarLength, ActualType(typeof(int))]
        public AimType Aim { get; set; }
        [Field(1)]
        public double TargetX { get; set; }
        [Field(2)]
        public double TargetY { get; set; }
        [Field(3)]
        public double TargetZ { get; set; }

        [Field(4)]
        public bool IsEntity { get; set; }

        [Field(5), VarLength, Condition(nameof(IsEntity))]
        public int EntityId { get; set; }

        [Field(6), VarLength, ActualType(typeof(int)), Condition(nameof(IsEntity))]
        public AimType AimEntity { get; set; }

        public FacePlayer()
        {

        }
    }

    public enum AimType : int
    {
        Feet = 0,
        Eyes
    }
}
