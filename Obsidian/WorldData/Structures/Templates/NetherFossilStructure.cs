using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Structures.Processors;

namespace Obsidian.WorldData.Structures.Templates;

/// <summary>
/// A bone fossil on the floor of a soul sand valley, like vanilla's <c>NetherFossilStructure</c>.
/// </summary>
[StructureType("minecraft:nether_fossil")]
public sealed class NetherFossilStructure : Structure
{
    /// <summary>The height the search for a floor starts from.</summary>
    public required IHeightProvider Height { get; init; }

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context)
    {
        var random = context.Random;
        var x = (context.ChunkX << 4) + random.NextInt(16);
        var z = (context.ChunkZ << 4) + random.NextInt(16);
        var seaLevel = context.Terrain.SeaLevel;
        var y = this.Height.Sample(random, context.Generation);
        var column = context.Terrain.GetBaseColumn(x, z);

        // Down to the first air above soul sand or a sturdy top.
        while (y > seaLevel)
        {
            var block = column.GetBlock(y);
            var below = column.GetBlock(--y);
            if (block.IsAir && (below.Material == Material.SoulSand || below.IsFaceSturdy(BlockFace.Up)))
                break;
        }

        if (y <= seaLevel)
            return null;

        var position = new Vector(x, y, z);
        return new StructureStub(position, builder => NetherFossilPiece.AddPieces(builder, random, position));
    }
}

/// <summary>
/// A nether fossil, like vanilla's <c>NetherFossilPieces.NetherFossilPiece</c>.
/// </summary>
public sealed class NetherFossilPiece : TemplateStructurePiece
{
    private static readonly string[] fossils = [.. Enumerable.Range(1, 14).Select(index => $"minecraft:nether_fossils/fossil_{index}")];

    private NetherFossilPiece(string templateId, Vector position, StructureRotation rotation)
        : base(0, templateId, MakeSettings(rotation), position)
    {
    }

    internal static void AddPieces(IStructurePieceAccessor pieces, IRandomSource random, Vector position)
    {
        var rotation = StructureRotationExtensions.Random(random);
        pieces.AddPiece(new NetherFossilPiece(fossils[random.NextInt(fossils.Length)], position, rotation));
    }

    /// <summary>
    /// Vanilla <c>postProcess</c>: every chunk the fossil reaches places all of it, then maybe a dried ghast on its floor.
    /// </summary>
    /// <remarks>Vanilla grows the chunk's shared box, which the structures placed after it in the chunk see as well.</remarks>
    public override void PostProcess(StructurePieceContext context)
    {
        var templateBox = this.Template.GetBoundingBox(this.PlaceSettings, this.TemplatePosition);
        var box = context.Box.Encapsulate(templateBox);
        this.PlaceTemplate(context, box, this.TemplatePosition);
        PlaceDriedGhast(context.Level, templateBox, box);
    }

    protected override void HandleDataMarker(string metadata, Vector position, StructurePieceContext context, BlockBox box)
    {
    }

    /// <summary>
    /// Vanilla <c>placeDriedGhast</c>: half the fossils get a dried ghast at a spot of their bottom layer, from a random
    /// seeded by the world seed and the fossil's center.
    /// </summary>
    private static void PlaceDriedGhast(IWorldGenLevel level, BlockBox templateBox, BlockBox box)
    {
        var center = templateBox.Center;
        var random = new LegacyRandomSource(level.Seed).ForkPositional().At(center.X, center.Y, center.Z);
        if (random.NextFloat() >= 0.5f)
            return;

        var x = templateBox.MinX + random.NextInt(templateBox.XSpan);
        var z = templateBox.MinZ + random.NextInt(templateBox.ZSpan);
        var position = new Vector(x, templateBox.MinY, z);
        if (level.GetBlock(position).IsAir && box.IsInside(position))
            level.SetBlock(position, BlocksRegistry.Get(Material.DriedGhast).Rotate(StructureRotationExtensions.Random(random)));
    }

    private static StructurePlaceSettings MakeSettings(StructureRotation rotation)
    {
        var settings = new StructurePlaceSettings { Rotation = rotation, Mirror = StructureMirror.None };
        settings.Processors.Add(BlockIgnoreProcessor.StructureAndAir);
        return settings;
    }
}
