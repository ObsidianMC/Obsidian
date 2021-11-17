namespace Obsidian.Utilities;

public static class XpHelper
{
    public static int TotalExperienceFromLevel(int level)
    {
        if (level < 0)
            throw new ArgumentOutOfRangeException(nameof(level));

        if (level < 17)
            return level * (level + 6);
        else if (level < 32)
            return (int)(level * (2.5f * level - 40.5f) + 360f);

        return (int)(level * (4.5f * level - 162.5f) + 2220f);
    }
    public static int ExperienceRequiredForNextLevel(int currentLevel)
    {
        if (currentLevel < 0)
            throw new ArgumentOutOfRangeException(nameof(currentLevel));

        if (currentLevel < 16)
            return 2 * currentLevel + 7;
        else if (currentLevel < 31)
            return 5 * currentLevel - 38;

        return 9 * currentLevel - 158;
    }
}
