using System;

namespace Obsidian.Utilities
{
    public static class XpHelper
    {
        public static int TotalExperienceFromLevel(int level) => level < 0
                ? throw new ArgumentOutOfRangeException(nameof(level))
                : level < 17
                    ? level * (level + 6)
                    : level < 32 ? (int)(level * (2.5f * level - 40.5f) + 360f) : (int)(level * (4.5f * level - 162.5f) + 2220f);

        public static int ExperienceRequiredForLevel(int currentLevel) => currentLevel < 0
                ? throw new ArgumentOutOfRangeException(nameof(currentLevel))
                : currentLevel < 16 ? 2 * currentLevel + 7 : currentLevel < 31 ? 5 * currentLevel - 38 : 9 * currentLevel - 158;
    }
}
