using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Buried fossils built from structure templates, like vanilla's FossilFeature: a bone template and a matching ore overlay,
/// each run through its processor list.
/// </summary>
/// <remarks>
/// Random draws, in vanilla order: rotation, template index, depth (<c>nextInt(10)</c>), then per template the palette pick
/// and each processor's rolls.
/// </remarks>
[ConfiguredFeatureClass("minecraft:fossil")]
public sealed class FossilFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:fossil";

    /// <summary>
    /// Template ids of the bone structures (e.g. <c>minecraft:fossil/spine_1</c>).
    /// </summary>
    public required string[] FossilStructures { get; init; }

    /// <summary>
    /// Template ids of the ore overlays, paired by index with <see cref="FossilStructures"/>.
    /// </summary>
    public required string[] OverlayStructures { get; init; }

    /// <summary>
    /// Processors applied to the fossil (e.g. <c>minecraft:fossil_rot</c>).
    /// </summary>
    public required StructureProcessorList FossilProcessors { get; init; }

    /// <summary>
    /// Processors applied to the overlay (e.g. <c>minecraft:fossil_coal</c>).
    /// </summary>
    public required StructureProcessorList OverlayProcessors { get; init; }

    /// <summary>
    /// The fossil is skipped when more of its bounding box corners are air, water or lava.
    /// </summary>
    public required int MaxEmptyCornersAllowed { get; init; }

    private StructureTemplate[] Fossils => field ??= Array.ConvertAll(this.FossilStructures, StructureRegistry.Get);

    private StructureTemplate[] Overlays => field ??= Array.ConvertAll(this.OverlayStructures, StructureRegistry.Get);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin))
            return false;

        var random = context.Random;
        var rotation = StructureRotationExtensions.Random(random);
        var index = random.NextInt(this.FossilStructures.Length);
        var fossil = this.Fossils[index];
        var overlay = this.Overlays[index];

        // Placement is limited to the origin's chunk and 16 blocks around it, over the full height.
        var chunkMinX = origin.X >> 4 << 4;
        var chunkMinZ = origin.Z >> 4 << 4;
        var settings = new StructurePlaceSettings
        {
            Rotation = rotation,
            BoundingBox = new BlockBox(
                new Vector(chunkMinX - 16, level.MinY, chunkMinZ - 16),
                new Vector(chunkMinX + 15 + 16, level.MinY + level.Height - 1, chunkMinZ + 15 + 16)),
            Random = random
        };

        // Bury the fossil 15-24 blocks below the lowest surface (OCEAN_FLOOR_WG) under its footprint.
        var size = fossil.GetSize(rotation);
        var corner = origin + (-size.X / 2, 0, -size.Z / 2);
        var lowest = origin.Y;
        for (var x = 0; x < size.X; x++)
        {
            for (var z = 0; z < size.Z; z++)
                lowest = Math.Min(lowest, level.GetHeight(HeightmapType.OceanFloorWG, corner.X + x, corner.Z + z));
        }

        var y = Math.Max(lowest - 15 - random.NextInt(10), level.MinY + 10);
        var zero = fossil.GetZeroPositionWithTransform(corner with { Y = y }, rotation);
        if (CountEmptyCorners(level, fossil.GetBoundingBox(settings, zero)) > this.MaxEmptyCornersAllowed)
            return false;

        settings.SetProcessors(this.FossilProcessors);
        fossil.PlaceInWorld(level, zero, zero, settings);
        settings.SetProcessors(this.OverlayProcessors);
        overlay.PlaceInWorld(level, zero, zero, settings);
        return true;
    }

    private static int CountEmptyCorners(IWorldGenLevel level, BlockBox box)
    {
        var empty = 0;
        foreach (var corner in box.Corners())
        {
            var block = level.GetBlock(corner);
            if (block.IsAir || block.Material is Material.Lava or Material.Water)
                empty++;
        }

        return empty;
    }
}
