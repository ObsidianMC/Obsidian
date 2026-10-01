using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Replaces template blocks by the first matching <see cref="ProcessorRule"/>.
/// </summary>
/// <remarks>
/// Rule tests get a random seeded from the block's world position (vanilla <c>RandomSource.create(Mth.getSeed(pos))</c>),
/// so the placement random isn't consumed.
/// </remarks>
[ConfiguredFeatureProperty("minecraft:rule")]
public sealed class RuleProcessor : StructureProcessor
{
    public required ProcessorRule[] Rules { get; init; }

    public override StructureBlockInfo? ProcessBlock(IWorldGenLevel level, Vector origin, Vector pivot, StructureBlockInfo original,
        StructureBlockInfo current, StructurePlaceSettings settings)
    {
        var position = current.Position;
        var random = new LegacyRandomSource(Mth.GetSeed(position.X, position.Y, position.Z));
        var existing = level.GetBlock(position);

        foreach (var rule in this.Rules)
        {
            if (rule.InputPredicate.Test(current.Block, random) && rule.LocationPredicate.Test(existing, random))
                return current with { Block = rule.OutputBlock };
        }

        return current;
    }
}

/// <summary>
/// One replacement of a <see cref="RuleProcessor"/>, like vanilla's <c>ProcessorRule</c>.
/// </summary>
/// <remarks>
/// Only the default position predicate (always true) and block entity modifier (pass-through) are supported, which is all
/// the vanilla processor lists in Obsidian's assets use.
/// </remarks>
public sealed class ProcessorRule
{
    /// <summary>Tests the template block.</summary>
    public required IRuleTest InputPredicate { get; init; }

    /// <summary>Tests the block currently in the world at the target position.</summary>
    public required IRuleTest LocationPredicate { get; init; }

    public required SimpleBlockState OutputState { get; init; }

    internal IBlock OutputBlock => field ??= BlocksRegistry.GetFromSimpleState(this.OutputState);
}
