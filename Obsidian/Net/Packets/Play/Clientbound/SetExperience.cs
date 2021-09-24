using Obsidian.Serialization.Attributes;
using Obsidian.Utilities;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SetExperience : IClientboundPacket
    {
        [Field(0)]
        public float ExperienceBar { get; }
        
        [Field(1), VarLength]
        public int Level { get; }
        
        [Field(2), VarLength]
        public int TotalExperience { get; }

        public int Id => 0x51;
        
        public SetExperience(float experienceBar, int level, int totalExperience)
        {
            ExperienceBar = experienceBar;
            Level = level;
            TotalExperience = totalExperience;
        }

        public static SetExperience FromLevel(int level) => new (0, level, XpHelper.LevelToExperience(level));
    }
}
