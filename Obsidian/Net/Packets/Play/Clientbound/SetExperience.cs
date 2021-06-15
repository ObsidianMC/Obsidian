using Obsidian.Serialization.Attributes;
using System;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public partial class SetExperience : IClientboundPacket
    {
        [Field(0)]
        public float ExperienceBar { get; set; }
        
        [Field(1), VarLength]
        public int Level { get; set; }
        
        [Field(2), VarLength]
        public int TotalExperience { get; set; }

        public int Id => 0x48;
        
        public SetExperience(float experienceBar, int level, int totalExperience)
        {
            ExperienceBar = experienceBar;
            Level = level;
            TotalExperience = totalExperience;
        }

        public static SetExperience FromLevel(int level) => new (0, level, (int)GetExperience(level));
        
        private static float GetExperience(int level)
        {
            float sqrtLevel = MathF.Sqrt(level);
            
            if (level < 16)
                return sqrtLevel + 6 * level;
            if(level > 17 & level < 31)
                return 2.5f * sqrtLevel - 40.5f * level + 360;
            if(level > 32)
                return 4.5f * sqrtLevel - 162.5f * level + 2220;
            return 0;
        }
    }
}
