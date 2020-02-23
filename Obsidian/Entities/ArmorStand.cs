using Obsidian.Entities.Transform;
using Obsidian.Util;

namespace Obsidian.Entities
{
    public class ArmorStand : Living
    {
        public StandProperties StandProperties { get; set; }

        public Rotation Head { get; set; }
        public Rotation Body { get; set; }
        public Rotation LeftArm { get; set; }
        public Rotation RightArm { get; set; }
        public Rotation LeftLeg { get; set; }
        public Rotation RightLeft { get; set; }
    }

    public struct StandProperties
    {
        public bool IsSmall;
        public bool HasArms;
        public bool NoBasePlate;
        public bool SetMarker;
    }
}
