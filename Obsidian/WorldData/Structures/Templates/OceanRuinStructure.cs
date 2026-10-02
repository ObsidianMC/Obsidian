using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.Providers.IntProviders;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.RuleTests;
using Obsidian.WorldData.Structures.Processors;

namespace Obsidian.WorldData.Structures.Templates;

/// <summary>
/// Ruins on the ocean floor, sometimes large with smaller ruins around, like vanilla's <c>OceanRuinStructure</c>.
/// </summary>
[StructureType("minecraft:ocean_ruin")]
public sealed class OceanRuinStructure : Structure
{
    public required OceanRuinType BiomeTemp { get; init; }

    public required float LargeProbability { get; init; }

    /// <summary>The chance for a large ruin to have smaller ruins around it.</summary>
    public required float ClusterProbability { get; init; }

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) =>
        OnTopOfChunkCenter(context, HeightmapType.OceanFloorWG, builder =>
        {
            var position = new Vector(context.ChunkX << 4, 90, context.ChunkZ << 4);
            var rotation = StructureRotationExtensions.Random(context.Random);
            OceanRuinPiece.AddPieces(position, rotation, builder, context.Random, this);
        });
}

public enum OceanRuinType
{
    Warm,
    Cold
}

/// <summary>
/// A ruin, like vanilla's <c>OceanRuinPieces.OceanRuinPiece</c>.
/// </summary>
public sealed class OceanRuinPiece : TemplateStructurePiece
{
    private static readonly StructureProcessor warmSuspiciousBlocks =
        ArchaeologyProcessor("minecraft:sand", "minecraft:suspicious_sand", "minecraft:archaeology/ocean_ruin_warm");

    private static readonly StructureProcessor coldSuspiciousBlocks =
        ArchaeologyProcessor("minecraft:gravel", "minecraft:suspicious_gravel", "minecraft:archaeology/ocean_ruin_cold");

    private static readonly string[] warmRuins = Names("warm_", 1, 2, 3, 4, 5, 6, 7, 8);
    private static readonly string[] brickRuins = Names("brick_", 1, 2, 3, 4, 5, 6, 7, 8);
    private static readonly string[] crackedRuins = Names("cracked_", 1, 2, 3, 4, 5, 6, 7, 8);
    private static readonly string[] mossyRuins = Names("mossy_", 1, 2, 3, 4, 5, 6, 7, 8);
    private static readonly string[] bigBrickRuins = Names("big_brick_", 1, 2, 3, 8);
    private static readonly string[] bigMossyRuins = Names("big_mossy_", 1, 2, 3, 8);
    private static readonly string[] bigCrackedRuins = Names("big_cracked_", 1, 2, 3, 8);
    private static readonly string[] bigWarmRuins = Names("big_warm_", 4, 5, 6, 7);

    private static readonly BlockSet ice = new("#minecraft:ice");

    private readonly bool isLarge;

    private OceanRuinPiece(string templateId, Vector position, StructureRotation rotation, float integrity, OceanRuinType biomeType, bool isLarge)
        : base(0, templateId, MakeSettings(rotation, integrity, biomeType), position) => this.isLarge = isLarge;

    /// <summary>Vanilla <c>OceanRuinPieces.addPieces</c>.</summary>
    internal static void AddPieces(Vector position, StructureRotation rotation, IStructurePieceAccessor pieces, IRandomSource random,
        OceanRuinStructure structure)
    {
        var isLarge = random.NextFloat() <= structure.LargeProbability;
        AddPiece(position, rotation, pieces, random, structure, isLarge, isLarge ? 0.9f : 0.8f);
        if (isLarge && random.NextFloat() <= structure.ClusterProbability)
            AddClusterRuins(random, rotation, position, structure, pieces);
    }

    /// <summary>
    /// Vanilla <c>postProcess</c>: the ruin moves down to the ocean floor, and further onto the seabed when most of it would
    /// float.
    /// </summary>
    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var position = this.TemplatePosition;
        position = position with { Y = level.GetHeight(HeightmapType.OceanFloorWG, position.X, position.Z) };
        var size = this.Template.Size;
        var corner = StructureTemplate.Transform(new Vector(size.X - 1, 0, size.Z - 1), StructureMirror.None, this.PlaceSettings.Rotation, Vector.Zero)
            + position;

        this.PlaceTemplate(context, context.Box, position with { Y = GetHeight(level, position, corner) });
    }

    protected override void HandleDataMarker(string metadata, Vector position, StructurePieceContext context, BlockBox box)
    {
        var level = context.Level;
        if (metadata == "chest")
        {
            var waterlogged = FeatureHelpers.IsWaterFluid(level.GetBlock(position));
            level.SetBlock(position, BlocksRegistry.Get(Material.Chest).WithProperty("waterlogged", waterlogged));
            SetChestLootTable(level, context.Random, position,
                this.isLarge ? "minecraft:chests/underwater_ruin_big" : "minecraft:chests/underwater_ruin_small");
        }
        else if (metadata == "drowned")
        {
            // Vanilla finalizes the spawn (equipment, baby chance) when the drowned is created.
            level.AddEntity(new GeneratedEntity("minecraft:drowned", new VectorF(position.X + 0.5f, position.Y, position.Z + 0.5f))
            {
                Data = new NbtCompound { new NbtTag<bool>("PersistenceRequired", true) }
            });

            level.SetBlock(position, position.Y > level.SeaLevel ? BlocksRegistry.Air : BlocksRegistry.Get(Material.Water));
        }
    }

    /// <summary>
    /// Vanilla <c>addClusterRuins</c>: 4 to 8 small ruins at random spots around the large one, unless they'd overlap it.
    /// </summary>
    private static void AddClusterRuins(IRandomSource random, StructureRotation rotation, Vector position, OceanRuinStructure structure,
        IStructurePieceAccessor pieces)
    {
        var origin = position with { Y = 90 };
        var far = StructureTemplate.Transform(new Vector(15, 0, 15), StructureMirror.None, rotation, Vector.Zero) + origin;
        var largeBox = BlockBox.FromCorners(origin, far);
        var corner = new Vector(Math.Min(origin.X, far.X), origin.Y, Math.Min(origin.Z, far.Z));
        var positions = AllPositions(random, corner);
        var count = FeatureHelpers.NextInt(random, 4, 8);

        for (var index = 0; index < count; index++)
        {
            if (positions.Count == 0)
                continue;

            var pick = random.NextInt(positions.Count);
            var ruinPosition = positions[pick];
            positions.RemoveAt(pick);

            var ruinRotation = StructureRotationExtensions.Random(random);
            var ruinFar = StructureTemplate.Transform(new Vector(5, 0, 6), StructureMirror.None, ruinRotation, Vector.Zero) + ruinPosition;
            if (!BlockBox.FromCorners(ruinPosition, ruinFar).Intersects(largeBox))
                AddPiece(ruinPosition, ruinRotation, pieces, random, structure, false, 0.8f);
        }
    }

    /// <summary>Vanilla <c>allPositions</c>: one candidate in each of the 8 areas around the large ruin.</summary>
    private static List<Vector> AllPositions(IRandomSource random, Vector corner) =>
    [
        corner + (-16 + FeatureHelpers.NextInt(random, 1, 8), 0, 16 + FeatureHelpers.NextInt(random, 1, 7)),
        corner + (-16 + FeatureHelpers.NextInt(random, 1, 8), 0, FeatureHelpers.NextInt(random, 1, 7)),
        corner + (-16 + FeatureHelpers.NextInt(random, 1, 8), 0, -16 + FeatureHelpers.NextInt(random, 4, 8)),
        corner + (FeatureHelpers.NextInt(random, 1, 7), 0, 16 + FeatureHelpers.NextInt(random, 1, 7)),
        corner + (FeatureHelpers.NextInt(random, 1, 7), 0, -16 + FeatureHelpers.NextInt(random, 4, 6)),
        corner + (16 + FeatureHelpers.NextInt(random, 1, 7), 0, 16 + FeatureHelpers.NextInt(random, 3, 8)),
        corner + (16 + FeatureHelpers.NextInt(random, 1, 7), 0, FeatureHelpers.NextInt(random, 1, 7)),
        corner + (16 + FeatureHelpers.NextInt(random, 1, 7), 0, -16 + FeatureHelpers.NextInt(random, 4, 8))
    ];

    /// <summary>
    /// Vanilla <c>addPiece</c>: a warm ruin, or a cold ruin as three overlaid templates (brick, cracked, mossy).
    /// </summary>
    private static void AddPiece(Vector position, StructureRotation rotation, IStructurePieceAccessor pieces, IRandomSource random,
        OceanRuinStructure structure, bool isLarge, float integrity)
    {
        if (structure.BiomeTemp == OceanRuinType.Cold)
        {
            var bricks = isLarge ? bigBrickRuins : brickRuins;
            var cracked = isLarge ? bigCrackedRuins : crackedRuins;
            var mossy = isLarge ? bigMossyRuins : mossyRuins;
            var index = random.NextInt(bricks.Length);
            pieces.AddPiece(new OceanRuinPiece(bricks[index], position, rotation, integrity, structure.BiomeTemp, isLarge));
            pieces.AddPiece(new OceanRuinPiece(cracked[index], position, rotation, 0.7f, structure.BiomeTemp, isLarge));
            pieces.AddPiece(new OceanRuinPiece(mossy[index], position, rotation, 0.5f, structure.BiomeTemp, isLarge));
            return;
        }

        var ruins = isLarge ? bigWarmRuins : warmRuins;
        pieces.AddPiece(new OceanRuinPiece(ruins[random.NextInt(ruins.Length)], position, rotation, integrity, structure.BiomeTemp, isLarge));
    }

    /// <summary>
    /// Vanilla <c>getHeight</c>: the lowest seabed (below air, water and ice) over the ruin's footprint, used when more
    /// than the footprint's width minus 2 columns are over 2 blocks lower than the ocean floor height.
    /// </summary>
    private static int GetHeight(IWorldGenLevel level, Vector position, Vector corner)
    {
        var y = position.Y;
        var lowest = 512;
        var floor = y - 1;
        var lowColumns = 0;

        var footprint = BlockBox.FromCorners(position, corner);
        foreach (var column in FeatureHelpers.BetweenClosed(footprint.Min, footprint.Max))
        {
            var columnY = position.Y - 1;
            var block = level.GetBlock(column with { Y = columnY });
            while ((block.IsAir || FeatureHelpers.IsWaterFluid(block) || ice.Contains(block)) && columnY > level.MinY + 1)
                block = level.GetBlock(column with { Y = --columnY });

            lowest = Math.Min(lowest, columnY);
            if (columnY < floor - 2)
                lowColumns++;
        }

        var width = Math.Abs(position.X - corner.X);
        return floor - lowest > 2 && lowColumns > width - 2 ? lowest + 1 : y;
    }

    private static StructurePlaceSettings MakeSettings(StructureRotation rotation, float integrity, OceanRuinType biomeType)
    {
        var settings = new StructurePlaceSettings { Rotation = rotation, Mirror = StructureMirror.None };
        settings.Processors.Add(new BlockRotProcessor { Type = "minecraft:block_rot", Integrity = integrity });
        settings.Processors.Add(BlockIgnoreProcessor.StructureAndAir);
        settings.Processors.Add(biomeType == OceanRuinType.Cold ? coldSuspiciousBlocks : warmSuspiciousBlocks);
        return settings;
    }

    /// <summary>Vanilla <c>archyRuleProcessor</c>: up to 5 blocks become suspicious blocks with archaeology loot.</summary>
    private static StructureProcessor ArchaeologyProcessor(string block, string suspicious, string lootTable) => new CappedProcessor
    {
        Type = "minecraft:capped",
        Delegate = new RuleProcessor
        {
            Type = "minecraft:rule",
            Rules =
            [
                new ProcessorRule
                {
                    InputPredicate = new BlockMatchTest { Block = block },
                    LocationPredicate = new AlwaysTrueTest(),
                    OutputState = new SimpleBlockState { Name = suspicious },
                    BlockEntityModifier = new AppendLootModifier { LootTable = lootTable }
                }
            ]
        },
        Limit = new ConstantIntProvider { Value = 5 }
    };

    private static string[] Names(string prefix, params int[] numbers) =>
        [.. numbers.Select(number => $"minecraft:underwater_ruin/{prefix}{number}")];
}
