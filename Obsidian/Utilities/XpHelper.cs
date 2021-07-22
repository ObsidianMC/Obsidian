using System;

namespace Obsidian.Utilities
{
    public static class XpHelper
    {
        public static int LevelToExperience(int level)
        {
            if (level < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(level));
            }
            else if (level < 17)
            {
                return level * (level + 6);
            }
            else if (level < 32)
            {
                return (int)(level * (2.5f * level - 40.5f) + 360f);
            }
            else
            {
                return (int)(level * (4.5f * level - 162.5f) + 2220f);
            }
        }

        public static int ExperienceForNextLevel(int currentLevel)
        {
            if (currentLevel < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(currentLevel));
            }
            else if (currentLevel < 16)
            {
                return 2 * currentLevel + 7;
            }
            else if (currentLevel < 31)
            {
                return 5 * currentLevel - 38;
            }
            else
            {
                return 9 * currentLevel - 158;
            }
        }
    }
}
