using Obsidian.API;
using Obsidian.API.Loot;
using Obsidian.WorldData.Generators.Mojang;
using System.Runtime.CompilerServices;

namespace Obsidian.WorldData;

/// <summary>
/// Per-level loot state shared by container and block loot, so tables that name a random sequence draw from one
/// owner, like vanilla's level random sequences.
/// </summary>
internal static class LevelLoot
{
    // Vanilla shares one set of random sequences between a server's levels and saves it; these live per level, in memory.
    private static readonly ConditionalWeakTable<ILevel, RandomSequences> sequences = [];

    public static RandomSequences Sequences(ILevel level) =>
        sequences.GetValue(level, level => new RandomSequences(RandomState.ParseSeed(level.Seed)));
}
