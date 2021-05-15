using System;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class SetExperience : ISerializablePacket
    {
        [Field(0)] public float ExperienceBar { get; set; }
        
        [VarLength] [Field(1)] public int Level { get; set; }
        
        [VarLength] [Field(2)] public int TotalExperience { get; set; }
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
        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;
        
        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}