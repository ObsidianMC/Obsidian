using Obsidian.API.World.Generator.RandomSources;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// Settings shared by every carver, read from vanilla's <c>worldgen/configured_carver</c> files.
/// </summary>
internal class CarverConfiguration
{
    /// <summary>
    /// Chance that a chunk starts this carver.
    /// </summary>
    public required float Probability { get; init; }

    public required HeightProvider Y { get; init; }

    public required FloatProvider YScale { get; init; }

    /// <summary>
    /// Carved positions at or below this level become lava.
    /// </summary>
    public required CarverAnchor LavaLevel { get; init; }

    /// <summary>
    /// Whether this carver may replace a block, by registry id; ids past the end can't be replaced.
    /// </summary>
    public required bool[] Replaceable { get; init; }

    public bool CanReplace(IBlock block)
    {
        var id = block.RegistryId;
        return (uint)id < (uint)this.Replaceable.Length && this.Replaceable[id];
    }
}

internal sealed class CaveCarverConfiguration : CarverConfiguration
{
    public required FloatProvider HorizontalRadiusMultiplier { get; init; }

    public required FloatProvider VerticalRadiusMultiplier { get; init; }

    public required FloatProvider FloorLevel { get; init; }
}

internal sealed class CanyonCarverConfiguration : CarverConfiguration
{
    public required FloatProvider VerticalRotation { get; init; }

    public required FloatProvider DistanceFactor { get; init; }

    public required FloatProvider Thickness { get; init; }

    public required int WidthSmoothness { get; init; }

    public required FloatProvider HorizontalRadiusFactor { get; init; }

    public required float VerticalRadiusDefaultFactor { get; init; }

    public required float VerticalRadiusCenterFactor { get; init; }
}

/// <summary>
/// A Y level relative to the bottom or top of the world.
/// </summary>
internal readonly record struct CarverAnchor(int Value, CarverAnchorKind Kind)
{
    public int Resolve(int minY, int height) => this.Kind switch
    {
        CarverAnchorKind.AboveBottom => minY + this.Value,
        CarverAnchorKind.BelowTop => height - 1 + minY - this.Value,
        _ => this.Value
    };

    public static CarverAnchor Parse(JsonElement element)
    {
        if (element.TryGetProperty("absolute", out var absolute))
            return new(absolute.GetInt32(), CarverAnchorKind.Absolute);

        if (element.TryGetProperty("above_bottom", out var aboveBottom))
            return new(aboveBottom.GetInt32(), CarverAnchorKind.AboveBottom);

        return new(element.GetProperty("below_top").GetInt32(), CarverAnchorKind.BelowTop);
    }
}

internal enum CarverAnchorKind
{
    Absolute,
    AboveBottom,
    BelowTop
}

/// <summary>
/// Samples a float, like vanilla's FloatProvider. Supports the types used by vanilla carvers.
/// </summary>
internal abstract class FloatProvider
{
    public abstract float Sample(IRandomSource random);

    public static FloatProvider Parse(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Number)
            return new Constant(element.GetSingle());

        return element.GetProperty("type").GetString() switch
        {
            "minecraft:constant" => new Constant(element.GetProperty("value").GetSingle()),
            "minecraft:uniform" => new Uniform(element.GetProperty("min_inclusive").GetSingle(), element.GetProperty("max_exclusive").GetSingle()),
            "minecraft:trapezoid" => new Trapezoid(element.GetProperty("min").GetSingle(), element.GetProperty("max").GetSingle(),
                element.GetProperty("plateau").GetSingle()),
            var type => throw new NotSupportedException($"Unsupported float provider '{type}'.")
        };
    }

    private sealed class Constant(float value) : FloatProvider
    {
        private readonly float value = value;

        public override float Sample(IRandomSource random) => this.value;
    }

    private sealed class Uniform(float minInclusive, float maxExclusive) : FloatProvider
    {
        private readonly float minInclusive = minInclusive;
        private readonly float maxExclusive = maxExclusive;

        public override float Sample(IRandomSource random) => random.NextFloat() * (this.maxExclusive - this.minInclusive) + this.minInclusive;
    }

    private sealed class Trapezoid(float min, float max, float plateau) : FloatProvider
    {
        private readonly float min = min;
        private readonly float max = max;
        private readonly float plateau = plateau;

        public override float Sample(IRandomSource random)
        {
            var range = this.max - this.min;
            var slope = (range - this.plateau) / 2.0f;
            var rest = range - slope;
            return this.min + random.NextFloat() * rest + random.NextFloat() * slope;
        }
    }
}

/// <summary>
/// Samples a Y level, like vanilla's HeightProvider. Supports the types used by vanilla carvers.
/// </summary>
internal abstract class HeightProvider
{
    public abstract int Sample(IRandomSource random, int minY, int height);

    public static HeightProvider Parse(JsonElement element)
    {
        if (!element.TryGetProperty("type", out var type))
            return new Constant(CarverAnchor.Parse(element));

        return type.GetString() switch
        {
            "minecraft:constant" => new Constant(CarverAnchor.Parse(element.GetProperty("value"))),
            "minecraft:uniform" => new Uniform(CarverAnchor.Parse(element.GetProperty("min_inclusive")),
                CarverAnchor.Parse(element.GetProperty("max_inclusive"))),
            var name => throw new NotSupportedException($"Unsupported height provider '{name}'.")
        };
    }

    private sealed class Constant(CarverAnchor value) : HeightProvider
    {
        private readonly CarverAnchor value = value;

        public override int Sample(IRandomSource random, int minY, int height) => this.value.Resolve(minY, height);
    }

    private sealed class Uniform(CarverAnchor minInclusive, CarverAnchor maxInclusive) : HeightProvider
    {
        private readonly CarverAnchor minInclusive = minInclusive;
        private readonly CarverAnchor maxInclusive = maxInclusive;

        public override int Sample(IRandomSource random, int minY, int height)
        {
            var min = this.minInclusive.Resolve(minY, height);
            var max = this.maxInclusive.Resolve(minY, height);

            // Vanilla returns the minimum for an empty range instead of throwing.
            return min > max ? min : random.NextInt(max - min + 1) + min;
        }
    }
}
