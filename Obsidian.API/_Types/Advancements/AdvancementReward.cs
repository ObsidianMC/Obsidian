using System.Collections.Generic;

namespace Obsidian.API.Advancements;

public sealed class AdvancementReward
{
    public AdvancementReward(int? experience, List<string> loot, List<string> recipes)
    {
        Experience = experience;
        Loot = loot;
        Recipes = recipes;
    }

    public int? Experience { get; init; }

    public List<string> Loot { get; init; }

    public List<string> Recipes { get; init; }
}
