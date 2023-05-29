namespace Obsidian.Utilities;

public static class XpHelper
{
    public static int TotalExperienceFromLevel(int level)
    {
        return level switch
        {
            < 0 => throw new ArgumentOutOfRangeException(nameof(level)),
            < 17 => level * (level + 6),
            < 32 => (int)(level * (2.5f * level - 40.5f) + 360f),
            _ => (int)(level * (4.5f * level - 162.5f) + 2220f)
        };
    }
    public static int ExperienceRequiredForNextLevel(int currentLevel)
    {
        return currentLevel switch
        {
            < 0 => throw new ArgumentOutOfRangeException(nameof(currentLevel)),
            < 16 => 2 * currentLevel + 7,
            < 31 => 5 * currentLevel - 38,
            _ => 9 * currentLevel - 158
        };
    }
}
