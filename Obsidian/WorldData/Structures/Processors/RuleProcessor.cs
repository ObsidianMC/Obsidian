using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;

namespace Obsidian.WorldData.Structures.Processors;

/// <summary>
/// Replaces template blocks by the first matching <see cref="ProcessorRule"/>, like vanilla's <c>RuleProcessor</c>.
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
        var random = PositionalRandom.Rent(position);
        try
        {
            var existing = level.GetBlock(position);
            foreach (var rule in this.Rules)
            {
                if (rule.Test(current.Block, existing, original.Position, position, pivot, random))
                    return new StructureBlockInfo(position, rule.OutputBlock, rule.BlockEntityModifier.Apply(random, current.Nbt));
            }

            return current;
        }
        finally
        {
            PositionalRandom.Return(random);
        }
    }
}

/// <summary>
/// One replacement of a <see cref="RuleProcessor"/>, like vanilla's <c>ProcessorRule</c>.
/// </summary>
public sealed class ProcessorRule
{
    /// <summary>Tests the template block.</summary>
    public required IRuleTest InputPredicate { get; init; }

    /// <summary>Tests the block currently in the world at the target position.</summary>
    public required IRuleTest LocationPredicate { get; init; }

    /// <summary>Tests the block's template and world positions against the pivot; always true by default.</summary>
    public IPosRuleTest PositionPredicate { get; init; } = PosAlwaysTrueTest.Instance;

    public required SimpleBlockState OutputState { get; init; }

    /// <summary>Changes the block entity data of the output; passed through unchanged by default.</summary>
    public IRuleBlockEntityModifier BlockEntityModifier { get; init; } = PassthroughModifier.Instance;

    internal IBlock OutputBlock => field ??= BlocksRegistry.GetFromSimpleState(this.OutputState);

    /// <summary>
    /// Vanilla <c>test</c>: the input, location and position predicates in that order, each drawing from
    /// <paramref name="random"/> only if the previous ones passed.
    /// </summary>
    public bool Test(IBlock input, IBlock existing, Vector local, Vector world, Vector pivot, IRandomSource random) =>
        this.InputPredicate.Test(input, random) && this.LocationPredicate.Test(existing, random)
        && this.PositionPredicate.Test(local, world, pivot, random);
}

/// <summary>
/// Tests positions for a <see cref="ProcessorRule"/>, like vanilla's <c>PosRuleTest</c>.
/// </summary>
public interface IPosRuleTest
{
    public string Type { get; }

    /// <param name="local">The block's template position.</param>
    /// <param name="world">The block's world position.</param>
    /// <param name="pivot">The placement pivot.</param>
    public bool Test(Vector local, Vector world, Vector pivot, IRandomSource random);
}

/// <summary>Accepts every position.</summary>
[ConfiguredFeatureProperty("minecraft:always_true")]
public sealed class PosAlwaysTrueTest : IPosRuleTest
{
    public static PosAlwaysTrueTest Instance { get; } = new();

    public string Type { get; init; } = "minecraft:always_true";

    public bool Test(Vector local, Vector world, Vector pivot, IRandomSource random) => true;
}

/// <summary>
/// Accepts a position with a chance interpolated between <see cref="MinChance"/> and <see cref="MaxChance"/> by its
/// Manhattan distance to the pivot, like vanilla's <c>LinearPosTest</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:linear_pos")]
public sealed class LinearPosTest : IPosRuleTest
{
    public string Type { get; init; } = "minecraft:linear_pos";

    public float MinChance { get; init; }

    public float MaxChance { get; init; }

    public int MinDist { get; init; }

    public int MaxDist { get; init; }

    public bool Test(Vector local, Vector world, Vector pivot, IRandomSource random)
    {
        var distance = (int)((float)Math.Abs(pivot.X - world.X) + Math.Abs(pivot.Y - world.Y) + Math.Abs(pivot.Z - world.Z));
        return random.NextFloat() <= ChanceAt(distance, this.MinDist, this.MaxDist, this.MinChance, this.MaxChance);
    }

    /// <summary>Vanilla <c>Mth.clampedLerp(Mth.inverseLerp(distance, min, max), minChance, maxChance)</c> in floats.</summary>
    internal static float ChanceAt(int distance, int minDistance, int maxDistance, float minChance, float maxChance)
    {
        var factor = ((float)distance - minDistance) / ((float)maxDistance - minDistance);
        return factor < 0f ? minChance : factor > 1f ? maxChance : minChance + factor * (maxChance - minChance);
    }
}

/// <summary>
/// Like <see cref="LinearPosTest"/>, but measures the distance to the pivot along one axis, like vanilla's
/// <c>AxisAlignedLinearPosTest</c>.
/// </summary>
[ConfiguredFeatureProperty("minecraft:axis_aligned_linear_pos")]
public sealed class AxisAlignedLinearPosTest : IPosRuleTest
{
    public string Type { get; init; } = "minecraft:axis_aligned_linear_pos";

    public float MinChance { get; init; }

    public float MaxChance { get; init; }

    public int MinDist { get; init; }

    public int MaxDist { get; init; }

    /// <summary><c>x</c>, <c>y</c> or <c>z</c>.</summary>
    public string Axis { get; init; } = "y";

    public bool Test(Vector local, Vector world, Vector pivot, IRandomSource random)
    {
        var distance = this.Axis switch
        {
            "x" => Math.Abs(world.X - pivot.X),
            "z" => Math.Abs(world.Z - pivot.Z),
            _ => Math.Abs(world.Y - pivot.Y)
        };

        return random.NextFloat() <= LinearPosTest.ChanceAt(distance, this.MinDist, this.MaxDist, this.MinChance, this.MaxChance);
    }
}

/// <summary>
/// Changes the block entity data of a <see cref="ProcessorRule"/>'s output, like vanilla's <c>RuleBlockEntityModifier</c>.
/// </summary>
public interface IRuleBlockEntityModifier
{
    public string Type { get; }

    /// <param name="data">The block's data; never modified, the result is a new compound when it changes.</param>
    public NbtCompound? Apply(IRandomSource random, NbtCompound? data);
}

/// <summary>Keeps the data.</summary>
[ConfiguredFeatureProperty("minecraft:passthrough")]
public sealed class PassthroughModifier : IRuleBlockEntityModifier
{
    public static PassthroughModifier Instance { get; } = new();

    public string Type { get; init; } = "minecraft:passthrough";

    public NbtCompound? Apply(IRandomSource random, NbtCompound? data) => data;
}

/// <summary>Replaces the data with an empty compound.</summary>
[ConfiguredFeatureProperty("minecraft:clear")]
public sealed class ClearModifier : IRuleBlockEntityModifier
{
    public string Type { get; init; } = "minecraft:clear";

    public NbtCompound? Apply(IRandomSource random, NbtCompound? data) => new();
}

/// <summary>Adds a loot table with a random seed, like vanilla's <c>AppendLoot</c> (suspicious sand and gravel).</summary>
[ConfiguredFeatureProperty("minecraft:append_loot")]
public sealed class AppendLootModifier : IRuleBlockEntityModifier
{
    public string Type { get; init; } = "minecraft:append_loot";

    public required string LootTable { get; init; }

    public NbtCompound? Apply(IRandomSource random, NbtCompound? data)
    {
        var result = data is null ? new NbtCompound() : NbtCopy.Copy(data);
        result.Remove("LootTable");
        result.Add("LootTable", new NbtTag<string>("LootTable", this.LootTable));
        result.Remove("LootTableSeed");
        result.Add("LootTableSeed", new NbtTag<long>("LootTableSeed", random.NextLong()));
        return result;
    }
}

/// <summary>
/// Merges fixed data into the block's data, like vanilla's <c>AppendStatic</c>.
/// </summary>
/// <remarks>
/// <see cref="Data"/> is the compound as JSON: numbers become ints when they're integral and doubles otherwise, since JSON
/// doesn't keep NBT's number types.
/// </remarks>
[ConfiguredFeatureProperty("minecraft:append_static")]
public sealed class AppendStaticModifier : IRuleBlockEntityModifier
{
    public string Type { get; init; } = "minecraft:append_static";

    public required string Data { get; init; }

    private NbtCompound Compound => field ??= JsonNbt.ToCompound(this.Data);

    public NbtCompound? Apply(IRandomSource random, NbtCompound? data)
    {
        if (data is null)
            return NbtCopy.Copy(this.Compound);

        var result = NbtCopy.Copy(data);
        NbtCopy.Merge(result, this.Compound);
        return result;
    }
}
