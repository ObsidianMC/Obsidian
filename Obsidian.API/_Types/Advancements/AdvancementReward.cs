namespace Obsidian.API.Advancements;

/// <summary>What a player gets for completing an advancement.</summary>
public sealed class AdvancementReward
{
    /// <summary>Creates a reward.</summary>
    /// <param name="experience">The experience points to give, or null for none.</param>
    /// <param name="loot">The ids of loot tables whose items are given.</param>
    /// <param name="recipes">The ids of recipes to unlock.</param>
    public AdvancementReward(int? experience, List<string> loot, List<string> recipes)
    {
        Experience = experience;
        Loot = loot;
        Recipes = recipes;
    }

    /// <summary>Gets the experience points given, or null for none.</summary>
    public int? Experience { get; init; }

    /// <summary>Gets the ids of loot tables whose items are given; ids that don't resolve are skipped.</summary>
    public List<string> Loot { get; init; }

    /// <summary>Gets the ids of recipes unlocked.</summary>
    public List<string> Recipes { get; init; }

    /// <summary>Gets the id of the function the advancement's definition names as a reward, or null.</summary>
    public string? Function { get; init; }
}
