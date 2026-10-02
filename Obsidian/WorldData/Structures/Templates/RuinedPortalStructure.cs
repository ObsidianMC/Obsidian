using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Features.RuleTests;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Structures.Processors;

namespace Obsidian.WorldData.Structures.Templates;

/// <summary>
/// A broken nether portal with netherrack spread around, like vanilla's <c>RuinedPortalStructure</c>.
/// </summary>
[StructureType("minecraft:ruined_portal")]
public sealed class RuinedPortalStructure : Structure
{
    private static readonly string[] portals = [.. Enumerable.Range(1, 10).Select(index => $"minecraft:ruined_portal/portal_{index}")];
    private static readonly string[] giantPortals = [.. Enumerable.Range(1, 3).Select(index => $"minecraft:ruined_portal/giant_portal_{index}")];

    /// <summary>The ways the portal can be placed, picked by weight.</summary>
    public required RuinedPortalSetup[] Setups { get; init; }

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context)
    {
        var random = context.Random;
        var setup = this.PickSetup(random);
        var properties = new RuinedPortalProperties
        {
            AirPocket = Sample(random, setup.AirPocketProbability),
            Mossiness = setup.Mossiness,
            Overgrown = setup.Overgrown,
            Vines = setup.Vines,
            ReplaceWithBlackstone = setup.ReplaceWithBlackstone
        };

        var templateId = random.NextFloat() < 0.05f ? giantPortals[random.NextInt(giantPortals.Length)] : portals[random.NextInt(portals.Length)];
        var template = StructureRegistry.Get(templateId);
        var rotation = StructureRotationExtensions.Random(random);
        var mirror = random.NextFloat() < 0.5f ? StructureMirror.None : StructureMirror.FrontBack;
        var pivot = new Vector(template.Size.X / 2, 0, template.Size.Z / 2);
        var chunkOrigin = new Vector(context.ChunkX << 4, 0, context.ChunkZ << 4);
        var box = template.GetBoundingBox(chunkOrigin, rotation, pivot, mirror);
        var center = box.Center;
        var surface = context.Terrain.GetBaseHeight(center.X, center.Z, RuinedPortalPiece.HeightmapFor(setup.Placement)) - 1;
        var y = FindSuitableY(random, context, setup.Placement, properties.AirPocket, surface, box.YSpan, box);
        var position = new Vector(chunkOrigin.X, y, chunkOrigin.Z);

        return new StructureStub(position, builder =>
        {
            if (setup.CanBeCold)
            {
                var biome = context.BiomeSource.GetNoiseBiome(context.Sampler, position.X >> 2, position.Y >> 2, position.Z >> 2);
                properties.Cold = BiomeTemperature.ColdEnoughToSnow(biome, position.X, position.Y, position.Z, context.Terrain.SeaLevel);
            }

            builder.AddPiece(new RuinedPortalPiece(position, setup.Placement, properties, templateId, rotation, mirror, pivot));
        });
    }

    /// <summary>Vanilla's setup pick: one <c>nextFloat</c> against the weights, only when there are several setups.</summary>
    private RuinedPortalSetup PickSetup(WorldgenRandom random)
    {
        if (this.Setups.Length == 1)
            return this.Setups[0];

        var total = 0f;
        foreach (var setup in this.Setups)
            total += setup.Weight;

        var pick = random.NextFloat();
        foreach (var setup in this.Setups)
        {
            pick -= setup.Weight / total;
            if (pick < 0f)
                return setup;
        }

        throw new InvalidOperationException("No ruined portal setup was picked.");
    }

    /// <summary>Vanilla <c>sample</c>: draws a float only for probabilities other than 0 and 1.</summary>
    private static bool Sample(WorldgenRandom random, float probability) =>
        probability != 0f && (probability == 1f || random.NextFloat() < probability);

    /// <summary>
    /// Vanilla <c>findSuitableY</c>: a height for the placement, lowered until 3 of the box's corner columns are solid there.
    /// </summary>
    private static int FindSuitableY(WorldgenRandom random, StructureGenerationContext context, RuinedPortalPlacement placement, bool airPocket,
        int surface, int height, BlockBox box)
    {
        var minY = context.MinY + 15;
        int y;
        switch (placement)
        {
            case RuinedPortalPlacement.InNether:
                if (airPocket)
                    y = FeatureHelpers.RandomBetweenInclusive(random, 32, 100);
                else if (random.NextFloat() < 0.5f)
                    y = FeatureHelpers.RandomBetweenInclusive(random, 27, 29);
                else
                    y = FeatureHelpers.RandomBetweenInclusive(random, 29, 100);
                break;
            case RuinedPortalPlacement.InMountain:
                y = RandomWithinInterval(random, 70, surface - height);
                break;
            case RuinedPortalPlacement.Underground:
                y = RandomWithinInterval(random, minY, surface - height);
                break;
            case RuinedPortalPlacement.PartlyBuried:
                y = surface - height + FeatureHelpers.RandomBetweenInclusive(random, 2, 8);
                break;
            default:
                y = surface;
                break;
        }

        var columns = new[]
        {
            context.Terrain.GetBaseColumn(box.MinX, box.MinZ),
            context.Terrain.GetBaseColumn(box.MaxX, box.MinZ),
            context.Terrain.GetBaseColumn(box.MinX, box.MaxZ),
            context.Terrain.GetBaseColumn(box.MaxX, box.MaxZ)
        };
        var heightmap = placement == RuinedPortalPlacement.OnOceanFloor ? HeightmapType.OceanFloorWG : HeightmapType.WorldSurfaceWG;

        for (; y > minY; y--)
        {
            var solid = 0;
            foreach (var column in columns)
            {
                if (WorldgenHeightmaps.Matches(heightmap, column.GetBlock(y)) && ++solid == 3)
                    return y;
            }
        }

        return y;
    }

    private static int RandomWithinInterval(IRandomSource random, int min, int max) =>
        min < max ? FeatureHelpers.RandomBetweenInclusive(random, min, max) : max;
}

/// <summary>
/// One way a <see cref="RuinedPortalStructure"/> can be placed, like vanilla's <c>RuinedPortalStructure.Setup</c>.
/// </summary>
public sealed class RuinedPortalSetup
{
    public required RuinedPortalPlacement Placement { get; init; }

    /// <summary>The chance that the portal keeps the template's air (otherwise only its blocks are placed).</summary>
    public required float AirPocketProbability { get; init; }

    /// <summary>The share of stone bricks and stairs that are mossy rather than cracked.</summary>
    public required float Mossiness { get; init; }

    /// <summary>Adds jungle leaves on the netherrack.</summary>
    public required bool Overgrown { get; init; }

    public required bool Vines { get; init; }

    /// <summary>In cold biomes, lava becomes netherrack and no magma is placed.</summary>
    public required bool CanBeCold { get; init; }

    public required bool ReplaceWithBlackstone { get; init; }

    public required float Weight { get; init; }
}

/// <summary>
/// Where a ruined portal is placed vertically, like vanilla's <c>RuinedPortalPiece.VerticalPlacement</c>.
/// </summary>
public enum RuinedPortalPlacement
{
    OnLandSurface,
    PartlyBuried,
    OnOceanFloor,
    InMountain,
    Underground,
    InNether
}

/// <summary>
/// The decided look of a ruined portal, like vanilla's <c>RuinedPortalPiece.Properties</c>.
/// </summary>
public sealed class RuinedPortalProperties
{
    public bool Cold { get; set; }

    public float Mossiness { get; init; }

    public bool AirPocket { get; init; }

    public bool Overgrown { get; init; }

    public bool Vines { get; init; }

    public bool ReplaceWithBlackstone { get; init; }
}

/// <summary>
/// A ruined portal, like vanilla's <c>RuinedPortalPiece</c>.
/// </summary>
public sealed class RuinedPortalPiece : TemplateStructurePiece
{
    private static readonly BlockSet featuresCannotReplace = new("#minecraft:features_cannot_replace");

    private static readonly float[] spreadChances = [1f, 1f, 1f, 1f, 1f, 1f, 1f, 0.9f, 0.9f, 0.8f, 0.7f, 0.6f, 0.4f, 0.2f];

    private readonly RuinedPortalPlacement placement;
    private readonly RuinedPortalProperties properties;

    internal RuinedPortalPiece(Vector position, RuinedPortalPlacement placement, RuinedPortalProperties properties, string templateId,
        StructureRotation rotation, StructureMirror mirror, Vector pivot)
        : base(0, templateId, MakeSettings(mirror, rotation, placement, pivot, properties), position)
    {
        this.placement = placement;
        this.properties = properties;
    }

    internal static HeightmapType HeightmapFor(RuinedPortalPlacement placement) =>
        placement == RuinedPortalPlacement.OnOceanFloor ? HeightmapType.OceanFloorWG : HeightmapType.WorldSurfaceWG;

    /// <summary>
    /// Vanilla <c>postProcess</c>: only the chunk holding the portal's center places it, in full, then spreads netherrack
    /// with drips below and adds vines and leaves.
    /// </summary>
    /// <remarks>Vanilla also leaves the grown box to the structures placed after it in the chunk.</remarks>
    public override void PostProcess(StructurePieceContext context)
    {
        var templateBox = this.Template.GetBoundingBox(this.PlaceSettings, this.TemplatePosition);
        if (!context.Box.IsInside(templateBox.Center))
            return;

        var level = context.Level;
        var random = context.Random;
        this.PlaceTemplate(context, context.Box.Encapsulate(templateBox), this.TemplatePosition);
        this.SpreadNetherrack(random, level);
        this.AddNetherrackDripColumnsBelowPortal(random, level);

        if (!this.properties.Vines && !this.properties.Overgrown)
            return;

        foreach (var position in FeatureHelpers.BetweenClosed(this.BoundingBox.Min, this.BoundingBox.Max))
        {
            if (this.properties.Vines)
                MaybeAddVines(random, level, position);

            if (this.properties.Overgrown)
                MaybeAddLeavesAbove(random, level, position);
        }
    }

    protected override void HandleDataMarker(string metadata, Vector position, StructurePieceContext context, BlockBox box)
    {
    }

    private static void MaybeAddVines(IRandomSource random, IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);
        if (block.IsAir || block.Material == Material.Vine)
            return;

        var direction = RandomHorizontalDirection(random);
        var neighborPosition = position.Offset(direction);
        // Vanilla checks the collision shape's face; full cubes (leaves included) and sturdy faces cover it.
        if (!level.GetBlock(neighborPosition).IsAir || !block.IsCollisionShapeFullBlock() && !block.IsFaceSturdy(direction))
            return;

        level.SetBlock(neighborPosition, BlocksRegistry.Get(Material.Vine).WithProperty(FeatureHelpers.FaceName(direction.Opposite()), true));
    }

    private static void MaybeAddLeavesAbove(IRandomSource random, IWorldGenLevel level, Vector position)
    {
        if (random.NextFloat() < 0.5f && level.GetBlock(position).Material == Material.Netherrack && level.GetBlock(position + Vector.Up).IsAir)
            level.SetBlock(position + Vector.Up, BlocksRegistry.Get(Material.JungleLeaves).WithProperty("persistent", true));
    }

    private void AddNetherrackDripColumnsBelowPortal(IRandomSource random, IWorldGenLevel level)
    {
        var box = this.BoundingBox;
        for (var x = box.MinX + 1; x < box.MaxX; x++)
        {
            for (var z = box.MinZ + 1; z < box.MaxZ; z++)
            {
                var position = new Vector(x, box.MinY, z);
                if (level.GetBlock(position).Material == Material.Netherrack)
                    this.AddNetherrackDripColumn(random, level, position + Vector.Down);
            }
        }
    }

    private void AddNetherrackDripColumn(IRandomSource random, IWorldGenLevel level, Vector position)
    {
        this.PlaceNetherrackOrMagma(random, level, position);
        var remaining = 8;
        while (remaining > 0 && random.NextFloat() < 0.5f)
        {
            position += Vector.Down;
            remaining--;
            this.PlaceNetherrackOrMagma(random, level, position);
        }
    }

    private void SpreadNetherrack(IRandomSource random, IWorldGenLevel level)
    {
        var onSurface = this.placement is RuinedPortalPlacement.OnLandSurface or RuinedPortalPlacement.OnOceanFloor;
        var box = this.BoundingBox;
        var center = box.Center;
        var reach = spreadChances.Length;
        var size = (box.XSpan + box.ZSpan) / 2;
        var shrink = random.NextInt(Math.Max(1, 8 - size / 2));

        for (var x = center.X - reach; x <= center.X + reach; x++)
        {
            for (var z = center.Z - reach; z <= center.Z + reach; z++)
            {
                var distance = Math.Max(0, Math.Abs(x - center.X) + Math.Abs(z - center.Z) + shrink);
                if (distance >= reach || !(random.NextDouble() < spreadChances[distance]))
                    continue;

                var surface = level.GetHeight(HeightmapFor(this.placement), x, z) - 1;
                var position = new Vector(x, onSurface ? surface : Math.Min(box.MinY, surface), z);
                if (Math.Abs(position.Y - box.MinY) > 3 || !this.CanBeReplacedByNetherrackOrMagma(level, position))
                    continue;

                this.PlaceNetherrackOrMagma(random, level, position);
                if (this.properties.Overgrown)
                    MaybeAddLeavesAbove(random, level, position);

                this.AddNetherrackDripColumn(random, level, position + Vector.Down);
            }
        }
    }

    private bool CanBeReplacedByNetherrackOrMagma(IWorldGenLevel level, Vector position)
    {
        var block = level.GetBlock(position);
        return block.Material is not (Material.Air or Material.Obsidian)
            && !featuresCannotReplace.Contains(block)
            && (this.placement == RuinedPortalPlacement.InNether || block.Material != Material.Lava);
    }

    private void PlaceNetherrackOrMagma(IRandomSource random, IWorldGenLevel level, Vector position) =>
        level.SetBlock(position, BlocksRegistry.Get(!this.properties.Cold && random.NextFloat() < 0.07f ? Material.MagmaBlock : Material.Netherrack));

    private static StructurePlaceSettings MakeSettings(StructureMirror mirror, StructureRotation rotation, RuinedPortalPlacement placement,
        Vector pivot, RuinedPortalProperties properties)
    {
        List<ProcessorRule> rules = [ReplaceRule("minecraft:gold_block", 0.3f, "minecraft:air"), LavaRule(placement, properties)];
        if (!properties.Cold)
            rules.Add(ReplaceRule("minecraft:netherrack", 0.07f, "minecraft:magma_block"));

        var settings = new StructurePlaceSettings { Rotation = rotation, Mirror = mirror, RotationPivot = pivot };
        settings.Processors.Add(properties.AirPocket ? BlockIgnoreProcessor.StructureBlock : BlockIgnoreProcessor.StructureAndAir);
        settings.Processors.Add(new RuleProcessor { Type = "minecraft:rule", Rules = [.. rules] });
        settings.Processors.Add(new BlockAgeProcessor { Type = "minecraft:block_age", Mossiness = properties.Mossiness });
        settings.Processors.Add(new ProtectedBlocksProcessor { Type = "minecraft:protected_blocks", Value = featuresCannotReplace });
        settings.Processors.Add(LavaSubmergedBlockProcessor.Instance);
        if (properties.ReplaceWithBlackstone)
            settings.Processors.Add(BlackstoneReplaceProcessor.Instance);

        return settings;
    }

    private static ProcessorRule LavaRule(RuinedPortalPlacement placement, RuinedPortalProperties properties)
    {
        if (placement == RuinedPortalPlacement.OnOceanFloor)
            return ReplaceRule("minecraft:lava", "minecraft:magma_block");

        return properties.Cold ? ReplaceRule("minecraft:lava", "minecraft:netherrack") : ReplaceRule("minecraft:lava", 0.2f, "minecraft:magma_block");
    }

    private static ProcessorRule ReplaceRule(string block, float probability, string output) => new()
    {
        InputPredicate = new RandomBlockMatchTest { Block = block, Probability = probability },
        LocationPredicate = new AlwaysTrueTest(),
        OutputState = new SimpleBlockState { Name = output }
    };

    private static ProcessorRule ReplaceRule(string block, string output) => new()
    {
        InputPredicate = new BlockMatchTest { Block = block },
        LocationPredicate = new AlwaysTrueTest(),
        OutputState = new SimpleBlockState { Name = output }
    };
}
