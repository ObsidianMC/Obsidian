using Obsidian.API.World.Generator.RandomSources;
using System.Threading;

namespace Obsidian.API.Loot;

/// <summary>
/// A world's named random sequences, like vanilla's <c>RandomSequences</c>: each id gets one random source, seeded from
/// the world seed and the id, that continues where the previous use left off. Unseeded loot tables draw from the
/// sequence named by their <see cref="LootTable.RandomSequence"/>.
/// </summary>
/// <remarks>
/// Vanilla saves the sequences' state with the world; this class keeps them in memory only.
/// </remarks>
public sealed class RandomSequences
{
    private readonly long worldSeed;
    private readonly Dictionary<string, IRandomSource> sequences = [];
    private readonly Lock sequencesLock = new();

    public RandomSequences(long worldSeed) => this.worldSeed = worldSeed;

    /// <summary>
    /// Returns the sequence for <paramref name="id"/> (e.g. <c>minecraft:chests/simple_dungeon</c>), creating it on
    /// first use. The returned source is shared and not thread-safe.
    /// </summary>
    public IRandomSource Get(string id)
    {
        lock (this.sequencesLock)
        {
            if (!this.sequences.TryGetValue(id, out var sequence))
            {
                // Vanilla's defaults: no salt, world seed and sequence id both included.
                var seed = RandomSupport.UpgradeSeedTo128BitUnmixed(this.worldSeed).Xor(RandomSupport.SeedFromHashOf(id)).Mixed();
                this.sequences[id] = sequence = new XoroshiroRandomSource(seed);
            }

            return sequence;
        }
    }
}
