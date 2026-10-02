using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Structures.Processors;

namespace Obsidian.WorldData.Structures.Templates;

/// <summary>
/// A sunken or beached ship, like vanilla's <c>ShipwreckStructure</c>.
/// </summary>
[StructureType("minecraft:shipwreck")]
public sealed class ShipwreckStructure : Structure
{
    /// <summary>Beached ships sit on the surface rather than the ocean floor.</summary>
    public required bool IsBeached { get; init; }

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) =>
        OnTopOfChunkCenter(context, this.IsBeached ? HeightmapType.WorldSurfaceWG : HeightmapType.OceanFloorWG, builder =>
        {
            var rotation = StructureRotationExtensions.Random(context.Random);
            var position = new Vector(context.ChunkX << 4, 90, context.ChunkZ << 4);
            var piece = ShipwreckPiece.AddRandomPiece(position, rotation, builder, context.Random, this.IsBeached);
            if (!piece.IsTooBigToFitInWorldGenRegion)
                return;

            // Too big to measure the terrain when placed, so it's measured from the noise now.
            var box = piece.BoundingBox;
            var y = this.IsBeached
                // Vanilla passes the span where the Z coordinate goes (and the reverse); kept for parity.
                ? piece.CalculateBeachedPosition(GetLowestY(context, box.MinX, box.XSpan, box.MinZ, box.ZSpan), context.Random)
                : GetMeanFirstOccupiedHeight(context, box.MinX, box.XSpan, box.MinZ, box.ZSpan);

            piece.AdjustPositionHeight(y);
        });
}

/// <summary>
/// A shipwreck, like vanilla's <c>ShipwreckPieces.ShipwreckPiece</c>.
/// </summary>
public sealed class ShipwreckPiece : TemplateStructurePiece
{
    private const int MaxSizeInWorldGenRegion = 32;

    private static readonly Vector pivot = new(4, 0, 15);

    private static readonly string[] beached =
    [
        "with_mast", "sideways_full", "sideways_fronthalf", "sideways_backhalf", "rightsideup_full", "rightsideup_fronthalf",
        "rightsideup_backhalf", "with_mast_degraded", "rightsideup_full_degraded", "rightsideup_fronthalf_degraded",
        "rightsideup_backhalf_degraded"
    ];

    private static readonly string[] ocean =
    [
        "with_mast", "upsidedown_full", "upsidedown_fronthalf", "upsidedown_backhalf", "sideways_full", "sideways_fronthalf",
        "sideways_backhalf", "rightsideup_full", "rightsideup_fronthalf", "rightsideup_backhalf", "with_mast_degraded",
        "upsidedown_full_degraded", "upsidedown_fronthalf_degraded", "upsidedown_backhalf_degraded", "sideways_full_degraded",
        "sideways_fronthalf_degraded", "sideways_backhalf_degraded", "rightsideup_full_degraded", "rightsideup_fronthalf_degraded",
        "rightsideup_backhalf_degraded"
    ];

    private static readonly Dictionary<string, string> markerLootTables = new()
    {
        ["map_chest"] = "minecraft:chests/shipwreck_map",
        ["treasure_chest"] = "minecraft:chests/shipwreck_treasure",
        ["supply_chest"] = "minecraft:chests/shipwreck_supply"
    };

    private readonly bool isBeached;

    private ShipwreckPiece(string templateId, Vector position, StructureRotation rotation, bool isBeached)
        : base(0, templateId, MakeSettings(rotation), position) => this.isBeached = isBeached;

    /// <summary>Vanilla <c>isTooBigToFitInWorldGenRegion</c>: wider or taller than 32 blocks.</summary>
    internal bool IsTooBigToFitInWorldGenRegion => this.Template.Size.X > MaxSizeInWorldGenRegion || this.Template.Size.Y > MaxSizeInWorldGenRegion;

    internal static ShipwreckPiece AddRandomPiece(Vector position, StructureRotation rotation, IStructurePieceAccessor pieces,
        IRandomSource random, bool isBeached)
    {
        var names = isBeached ? beached : ocean;
        var piece = new ShipwreckPiece("minecraft:shipwreck/" + names[random.NextInt(names.Length)], position, rotation, isBeached);
        pieces.AddPiece(piece);
        return piece;
    }

    /// <summary>Vanilla <c>calculateBeachedPosition</c>: half the ship's height below the surface, plus 0 to 2.</summary>
    internal int CalculateBeachedPosition(int surface, IRandomSource random) => surface - this.Template.Size.Y / 2 - random.NextInt(3);

    /// <summary>Vanilla <c>adjustPositionHeight</c>: moves the template, but not the bounding box.</summary>
    internal void AdjustPositionHeight(int y) => this.SetTemplatePosition(this.TemplatePosition with { Y = y });

    /// <summary>
    /// Vanilla <c>postProcess</c>: ships that fit in the region sit at the mean (or, beached, lowest) height of their
    /// footprint, measured in the region being decorated.
    /// </summary>
    public override void PostProcess(StructurePieceContext context)
    {
        // Big ships got their height while the structure was built.
        if (this.IsTooBigToFitInWorldGenRegion)
        {
            this.PlaceTemplate(context, context.Box, this.TemplatePosition);
            return;
        }

        var level = context.Level;
        var size = this.Template.Size;
        var heightmap = this.isBeached ? HeightmapType.WorldSurfaceWG : HeightmapType.OceanFloorWG;
        var position = this.TemplatePosition;
        var lowest = level.MinY + level.Height;
        var mean = 0;
        var area = size.X * size.Z;

        if (area == 0)
        {
            mean = level.GetHeight(heightmap, position.X, position.Z);
        }
        else
        {
            foreach (var column in FeatureHelpers.BetweenClosed(position, position + (size.X - 1, 0, size.Z - 1)))
            {
                var height = level.GetHeight(heightmap, column.X, column.Z);
                mean += height;
                lowest = Math.Min(lowest, height);
            }

            mean /= area;
        }

        var y = this.isBeached ? this.CalculateBeachedPosition(lowest, context.Random) : mean;
        this.PlaceTemplate(context, context.Box, position with { Y = y });
    }

    protected override void HandleDataMarker(string metadata, Vector position, StructurePieceContext context, BlockBox box)
    {
        if (markerLootTables.TryGetValue(metadata, out var lootTable))
            FeatureHelpers.SetLootTable(context.Level, context.Random, position + Vector.Down, lootTable);
    }

    private static StructurePlaceSettings MakeSettings(StructureRotation rotation)
    {
        var settings = new StructurePlaceSettings { Rotation = rotation, Mirror = StructureMirror.None, RotationPivot = pivot };
        settings.Processors.Add(BlockIgnoreProcessor.StructureAndAir);
        return settings;
    }
}
