using System.Collections.Generic;

namespace Obsidian.API.Advancements
{
    public sealed class AdvancementReward
    {
        public int? Experience { get; init; }

        public List<string> Loot { get; init; }

        public List<string> Recipes { get; init; }
    }
}
