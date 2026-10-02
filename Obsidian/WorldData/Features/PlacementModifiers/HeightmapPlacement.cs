using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features.PlacementModifiers;

/// <summary>
/// Moves the position onto the given heightmap, dropping it when the column is empty.
/// </summary>
[ConfiguredFeatureProperty("minecraft:heightmap")]
public sealed class HeightmapPlacement : SinglePlacementModifierBase
{
    public override string Type => "minecraft:heightmap";

    public required HeightmapType Heightmap { get; init; }

    public override Vector? GetPosition(PlacementContext context, IRandomSource random, Vector position)
    {
        var height = context.Level.GetHeight(this.Heightmap, position.X, position.Z);
        return height > context.Level.MinY ? new Vector(position.X, height, position.Z) : null;
    }
}
