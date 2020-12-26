using Obsidian.API;
using Obsidian.Util.Registry.Enums;

namespace Obsidian.Util.Registry
{
    public class BlockPropertiesJson
    {
        public int Stage { get; set; }
        public int Level { get; set; }
        public int Note { get; set; }
        public int Age { get; set; }
        public int Power { get; set; }
        public int Moisture { get; set; }
        public int Rotation { get; set; }
        public int Layers { get; set; }
        public int Bites { get; set; }
        public int Delay { get; set; }
        public int HoneyLevel { get; set; }
        public int Distance { get; set; }

        public string East { get; set; }
        public string North { get; set; }
        public string South { get; set; }
        public string Up { get; set; }
        public string West { get; set; }
        public string Bottom { get; set; }

        public bool Snowy { get; set; }
        public bool Powered { get; set; }
        public bool Triggered { get; set; }
        public bool Occupied { get; set; }
        public bool Unstable { get; set; }
        public bool Waterlogged { get; set; }
        public bool Lit { get; set; }
        public bool SignalFire { get; set; }
        public bool Hanging { get; set; }
        public bool Opened { get; set; }
        public bool HasRecord { get; set; }
        public bool Locked { get; set; }
        public bool HasBottle0 { get; set; }
        public bool HasBottle1 { get; set; }
        public bool HasBottle2 { get; set; }
        public bool HasBook { get; set; }
        public bool Disarmed { get; set; }
        public bool Attached { get; set; }

        public Axis Axis { get; set; }
        public BlockFace Facing { get; set; }
        public Instruments Instrument { get; set; }
        public Part Part { get; set; }
        public Shape Shape { get; set; }
        public EHalf Half { get; set; }
        public MinecraftType Type { get; set; }
        public Hinge Hinge { get; set; }
        public Face Face { get; set; }
        public Mode Mode { get; set; }
        public Attachment Attachment { get; set; }
    }
}
