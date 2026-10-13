using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    // Vanilla's ServerLevel.randValue, which picks the blocks random ticks reach.
    private int randomTickValue = Random.Shared.Next();

    /// <summary>
    /// The random plants grow with on the level's tick, from random ticks and bone meal, like vanilla's level random.
    /// </summary>
    internal IRandomSource GrowthRandom { get; } = new LegacyRandomSource(Random.Shared.NextInt64());

    /// <summary>
    /// Random-ticks the chunks that tick, like vanilla's <c>ServerLevel.tickChunk</c>: the game rule's number of
    /// blocks in each section that isn't empty, of which the plants that grow on random ticks grow (see
    /// <see cref="Growth"/>).
    /// </summary>
    internal void TickRandomBlocks()
    {
        var speed = LevelData.GetIntegerRule("random_tick_speed", 3);
        if (speed <= 0)
            return;

        foreach (var chunk in Regions.Values.SelectMany(region => region.GeneratedChunks()).OfType<Chunk>())
        {
            if (!IsMobTicking(new VectorD(chunk.X * 16 + 8, 0, chunk.Z * 16 + 8)))
                continue;

            var sections = chunk.Sections;
            for (var index = 0; index < sections.Length; index++)
            {
                var section = sections[index];
                if (section.IsEmpty)
                    continue;

                for (var tick = 0; tick < speed; tick++)
                {
                    // Vanilla's getBlockRandomPos: x, z and y from a linear congruential sequence.
                    this.randomTickValue = this.randomTickValue * 3 + 1013904223;
                    var bits = this.randomTickValue >> 2;
                    var x = bits & 15;
                    var y = bits >> 16 & 15;
                    var z = bits >> 8 & 15;

                    var block = section.GetBlock(x, y, z);
                    if (!Growth.IsRandomlyTicking(block))
                        continue;

                    var position = new Vector(chunk.X * 16 + x, chunk.MinY + index * 16 + y, chunk.Z * 16 + z);
                    Growth.RandomTick(this, position, block, this.GrowthRandom);
                }
            }
        }
    }

    /// <summary>
    /// Uses bone meal on the block at <paramref name="position"/>, like vanilla's <c>BoneMealItem.growCrop</c>: when
    /// the block takes it, it grows on the level's next tick if its chance comes up, and players see the bone meal's
    /// particles and hear its sound.
    /// </summary>
    /// <returns>Whether the block takes bone meal, which uses it up.</returns>
    internal bool UseBoneMeal(Vector position)
    {
        if (GameEventBlock(position) is not IBlock block || !Growth.IsValidBonemealTarget(this, position, block))
            return false;

        // Grown on the tick, which random ticks grow on too, so the two never place a tree at once.
        EnqueueEntityAction(() =>
        {
            if (GameEventBlock(position) is IBlock current
                && current.Equals(block)
                && Growth.IsValidBonemealTarget(this, position, current))
            {
                Growth.ApplyBonemeal(this, position, current, this.GrowthRandom);
            }

            return ValueTask.CompletedTask;
        });

        BroadcastLevelEvent(Growth.BoneMealEvent, position, Growth.BoneMealParticles);

        return true;
    }
}
