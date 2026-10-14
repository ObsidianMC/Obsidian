namespace Obsidian.API.Progress;

/// <summary>Persistent vanilla statistics, addressed by registry identifiers rather than protocol ids.</summary>
public interface IStatistics
{
    /// <summary>Returns zero for a statistic without a saved value. The default type is minecraft:custom.</summary>
    int Get(string identifier, string type = "minecraft:custom");

    /// <summary>Increments a registered statistic, saturating at int.MaxValue. Amount must be nonnegative.</summary>
    void Increment(string identifier, int amount = 1, string type = "minecraft:custom");
}
