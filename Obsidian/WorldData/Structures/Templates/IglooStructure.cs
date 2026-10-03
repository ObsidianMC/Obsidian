using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Structures.Processors;

namespace Obsidian.WorldData.Structures.Templates;

/// <summary>
/// An igloo, sometimes with a ladder down to a basement, like vanilla's <c>IglooStructure</c>.
/// </summary>
[StructureType("minecraft:igloo")]
public sealed class IglooStructure : Structure
{
    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) =>
        OnTopOfChunkCenter(context, HeightmapType.WorldSurfaceWG, builder =>
        {
            var position = new Vector(context.ChunkX << 4, IglooPiece.GenerationHeight, context.ChunkZ << 4);
            var rotation = StructureRotationExtensions.Random(context.Random);
            IglooPiece.AddPieces(position, rotation, builder, context.Random);
        });
}

/// <summary>
/// A part of an igloo (top, ladder or laboratory), like vanilla's <c>IglooPieces.IglooPiece</c>.
/// </summary>
public sealed class IglooPiece : TemplateStructurePiece
{
    internal const int GenerationHeight = 90;

    private const string Top = "minecraft:igloo/top";
    private const string Ladder = "minecraft:igloo/middle";
    private const string Laboratory = "minecraft:igloo/bottom";

    private static readonly Dictionary<string, Vector> pivots = new()
    {
        [Top] = new Vector(3, 5, 5),
        [Ladder] = new Vector(1, 3, 1),
        [Laboratory] = new Vector(3, 6, 7)
    };

    private static readonly Dictionary<string, Vector> offsets = new()
    {
        [Top] = Vector.Zero,
        [Ladder] = new Vector(2, -3, 4),
        [Laboratory] = new Vector(0, -3, -2)
    };

    private IglooPiece(string templateId, Vector position, StructureRotation rotation, int down)
        : base(0, templateId, MakeSettings(rotation, templateId), position + offsets[templateId] + (0, -down, 0))
    {
    }

    /// <summary>
    /// Vanilla <c>IglooPieces.addPieces</c>: half the igloos get a laboratory 4 to 11 ladder segments below.
    /// </summary>
    internal static void AddPieces(Vector position, StructureRotation rotation, IStructurePieceAccessor pieces, IRandomSource random)
    {
        if (random.NextDouble() < 0.5)
        {
            var depth = random.NextInt(8) + 4;
            pieces.AddPiece(new IglooPiece(Laboratory, position, rotation, depth * 3));

            for (var segment = 0; segment < depth - 1; segment++)
                pieces.AddPiece(new IglooPiece(Ladder, position, rotation, segment * 3));
        }

        pieces.AddPiece(new IglooPiece(Top, position, rotation, 0));
    }

    /// <summary>
    /// Vanilla <c>IglooPiece.postProcess</c>: the igloo is moved onto the surface at its entrance for the placement, and the
    /// top's entrance is filled with snow unless it opens on a ladder or air.
    /// </summary>
    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var settings = MakeSettings(this.PlaceSettings.Rotation, this.TemplateName);
        var offset = offsets[this.TemplateName];
        var entrance = this.TemplatePosition + StructureTemplate.CalculateRelativePosition(settings, new Vector(3 - offset.X, 0, -offset.Z));
        var surface = level.GetHeight(HeightmapType.WorldSurfaceWG, entrance.X, entrance.Z);
        var position = this.TemplatePosition + (0, surface - GenerationHeight - 1, 0);

        this.PlaceTemplate(context, context.Box, position);

        if (this.TemplateName == Top)
        {
            var door = position + StructureTemplate.CalculateRelativePosition(settings, new Vector(3, 0, 5));
            var below = level.GetBlock(door + Vector.Down);
            if (!below.IsAir && below.Material != Material.Ladder)
                level.SetBlock(door, BlocksRegistry.Get(Material.SnowBlock));
        }
    }

    protected override void HandleDataMarker(string metadata, Vector position, StructurePieceContext context, BlockBox box)
    {
        if (metadata != "chest")
            return;

        context.Level.SetBlock(position, BlocksRegistry.Air);
        SetChestLootTable(context.Level, context.Random, position + Vector.Down, "minecraft:chests/igloo_chest");
    }

    private static StructurePlaceSettings MakeSettings(StructureRotation rotation, string templateId)
    {
        var settings = new StructurePlaceSettings
        {
            Rotation = rotation,
            Mirror = StructureMirror.None,
            RotationPivot = pivots[templateId],
            LiquidSettings = LiquidSettings.IgnoreWaterlogging
        };

        settings.Processors.Add(BlockIgnoreProcessor.StructureBlock);
        return settings;
    }
}
