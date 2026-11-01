using Microsoft.Extensions.Logging;
using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData;

/// <summary>
/// How plants grow on random ticks and with bone meal, like vanilla's <c>SaplingBlock</c>, <c>TreeGrower</c>,
/// <c>BonemealableBlock</c>s and <c>BoneMealItem</c>: saplings (a plugin's too) and the mangrove propagule, azaleas,
/// crops, grass blocks and short grass and ferns.
/// </summary>
/// <remarks>
/// Growing a plant draws from the random it's given in vanilla's order (the chance, then which tree, then the tree's
/// own draws), and places the features vanilla places, found by identifier like vanilla's registry lookups, so that
/// plugins' and data packs' overrides apply. Call it on the level's tick, holding the level's
/// <see cref="Fluids.LevelFluids.TickLock"/> from reading the block to growing it, which places the features in the
/// level's chunks (see <see cref="LiveFeatureLevel"/>).
/// </remarks>
internal static partial class Growth
{
    // Vanilla's LevelEvent.PARTICLES_AND_SOUND_PLANT_GROWTH, which bone meal sends with 15 particles.
    public const int BoneMealEvent = 1505;

    public const int BoneMealParticles = 15;

    // The light a sapling needs above it to grow (vanilla's SaplingBlock.randomTick).
    private const int SaplingLight = 9;

    private const string GrassBonemeal = "minecraft:grass_bonemeal";

    private enum Kind
    {
        None,
        Sapling,
        Propagule,
        Azalea,
        Crop,
        GrassBlock,
        TallGrass
    }

    private static readonly TreeGrower azalea = new() { Tree = "minecraft:azalea_tree" };

    /// <summary>
    /// Whether random ticks grow <paramref name="block"/>: saplings and the mangrove propagule.
    /// </summary>
    public static bool IsRandomlyTicking(IBlock block) => KindOf(block, out _) is Kind.Sapling or Kind.Propagule;

    /// <summary>
    /// The random tick of the vanilla class that owns <paramref name="block"/>'s <c>randomTick</c>, when Obsidian
    /// implements it (see <see cref="RandomTickBehaviors"/>); null otherwise, and for plugin blocks.
    /// </summary>
    public static IRandomTickBehavior? VanillaRandomTick(IBlock block) =>
        BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out _) ? null : BlocksRegistry.GetRandomTickBehavior(block.RegistryId);

    /// <summary>
    /// A random tick of a plant: a sapling in enough light grows a step one time in 7, and a propagule grows a step
    /// one time in 7 when it stands, or ages when it hangs.
    /// </summary>
    public static void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        switch (KindOf(block, out var grower))
        {
            case Kind.Sapling:
                if (RawBrightness(level, position + Vector.Up, level.SkyDarken()) >= SaplingLight && random.NextInt(7) == 0)
                    AdvanceTree(level, position, block, grower!, random);

                break;
            case Kind.Propagule when !IsHanging(block):
                if (random.NextInt(7) == 0)
                    AdvanceTree(level, position, block, grower!, random);

                break;
            case Kind.Propagule when Age(block) < 4:
                SetAndShow(level, position, block.WithProperty("age", Age(block) + 1));
                break;
        }
    }

    /// <summary>
    /// Whether bone meal does something to <paramref name="block"/>, which uses it up (vanilla's
    /// <c>isValidBonemealTarget</c>).
    /// </summary>
    public static bool IsValidBonemealTarget(AbstractLevel level, Vector position, IBlock block) =>
        KindOf(block, out _) switch
        {
            Kind.Sapling => true,
            Kind.Propagule => !IsHanging(block) || Age(block) < 4,
            Kind.Azalea => !BlockAt(level, position + Vector.Up).HasFluid(),
            Kind.Crop => Age(block) < MaxAge(block),
            Kind.GrassBlock => BlockAt(level, position + Vector.Up).IsAir,
            Kind.TallGrass => BlockAt(level, position + Vector.Up).IsAir,
            _ => false
        };

    /// <summary>
    /// Bone meal on a block that takes it (see <see cref="IsValidBonemealTarget"/>): it grows when its chance comes
    /// up, like vanilla's <c>BoneMealItem.growCrop</c>.
    /// </summary>
    public static void ApplyBonemeal(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var kind = KindOf(block, out var grower);

        // Vanilla's isBonemealSuccess: saplings, standing propagules and azaleas 45% of the time.
        var success = kind switch
        {
            Kind.Sapling or Kind.Azalea => random.NextFloat() < 0.45,
            Kind.Propagule => IsHanging(block) ? Age(block) < 4 : random.NextFloat() < 0.45,
            _ => true
        };

        if (!success)
            return;

        switch (kind)
        {
            case Kind.Sapling:
                AdvanceTree(level, position, block, grower!, random);
                break;
            case Kind.Propagule when IsHanging(block):
                SetAndShow(level, position, block.WithProperty("age", Age(block) + 1));
                break;
            case Kind.Propagule:
                AdvanceTree(level, position, block, grower!, random);
                break;
            case Kind.Azalea:
                GrowTree(level, position, block, azalea, random);
                break;
            case Kind.Crop:
                GrowCrop(level, position, block, random);
                break;
            case Kind.GrassBlock:
                GrowGrass(level, position, random);
                break;
            case Kind.TallGrass:
                GrowTallGrass(level, position, block);
                break;
        }
    }

    // A vanilla sapling is classified by the class that owns its random tick (SaplingBlock or MangrovePropaguleBlock),
    // and grows the tree the generated table names for it (vanilla's treeGrower).
    private static Kind KindOf(IBlock block, out TreeGrower? grower)
    {
        grower = null;

        if (BlocksRegistry.TryGetPluginBlock(block.GetHashCode(), out var type))
        {
            grower = type.Binding?.TreeGrower;
            return grower is null ? Kind.None : Kind.Sapling;
        }

        var owner = BlocksRegistry.GetRandomTickBehavior(block.RegistryId);
        if (ReferenceEquals(owner, RandomTickBehaviors.SaplingBlock) || ReferenceEquals(owner, RandomTickBehaviors.MangrovePropaguleBlock))
        {
            grower = BlocksRegistry.GetTreeGrower(block.RegistryId);
            if (grower is not null)
                return ReferenceEquals(owner, RandomTickBehaviors.MangrovePropaguleBlock) ? Kind.Propagule : Kind.Sapling;
        }

        return block.RegistryId switch
        {
            BlockIds.Azalea or BlockIds.FloweringAzalea => Kind.Azalea,
            BlockIds.Wheat or BlockIds.Carrots or BlockIds.Potatoes or BlockIds.Beetroots
                or BlockIds.TorchflowerCrop => Kind.Crop,
            BlockIds.GrassBlock => Kind.GrassBlock,
            BlockIds.ShortGrass or BlockIds.Fern => Kind.TallGrass,
            _ => Kind.None
        };
    }

    // Vanilla's SaplingBlock.advanceTree: a sapling at stage 0 moves to stage 1, which clients aren't told (it looks
    // the same); then it grows. A plugin's sapling without a stage grows at once.
    private static void AdvanceTree(
        AbstractLevel level,
        Vector position,
        IBlock block,
        TreeGrower grower,
        IRandomSource random)
    {
        if (block.GetProperty("stage") == "0")
        {
            level.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.SetBlock(position, block.WithProperty("stage", 1));
            return;
        }

        GrowTree(level, position, block, grower, random);
    }

    /// <summary>
    /// Vanilla's <c>TreeGrower.growTree</c>: four saplings in a square grow the mega tree from their north-west
    /// corner, else the sapling grows its tree, or the flowers' tree with a flower nearby. The saplings make way for
    /// the tree, and come back when it can't grow.
    /// </summary>
    /// <remarks>
    /// A tree that reaches a chunk that isn't loaded and complete doesn't grow, rather than grow cut off there: vanilla
    /// would have loaded the chunk.
    /// </remarks>
    /// <returns>Whether a tree grew.</returns>
    private static bool GrowTree(
        AbstractLevel level,
        Vector position,
        IBlock block,
        TreeGrower grower,
        IRandomSource random)
    {
        var live = new LiveFeatureLevel(level);
        var grew = PlaceTree(live, position, block, grower, random);

        if (live.ReachedUnloaded)
        {
            live.Rollback();
            grew = false;
        }

        live.Commit();
        return grew;
    }

    private static bool PlaceTree(
        LiveFeatureLevel live,
        Vector position,
        IBlock block,
        TreeGrower grower,
        IRandomSource random)
    {
        var features = PluginFeatures.CurrentRegistry.FeatureSet!;

        if (MegaTree(grower, random) is string mega && features.FindConfigured(mega) is not null)
        {
            for (var dx = 0; dx >= -1; dx--)
            {
                for (var dz = 0; dz >= -1; dz--)
                {
                    if (!IsTwoByTwo(live, block, position, dx, dz))
                        continue;

                    var corner = position + new Vector(dx, 0, dz);
                    Vector[] saplings =
                    [
                        corner,
                        corner + Vector.East,
                        corner + Vector.South,
                        corner + new Vector(1, 0, 1)
                    ];

                    foreach (var sapling in saplings)
                        live.SetBlock(sapling, BlocksRegistry.Air);

                    if (Place(features, mega, live, corner, random))
                        return true;

                    foreach (var sapling in saplings)
                        live.SetBlock(sapling, block);

                    return false;
                }
            }
        }

        if (Tree(grower, random, HasFlowers(live, position)) is not string tree
            || features.FindConfigured(tree) is null)
        {
            return false;
        }

        // The sapling's fluid stays where the tree grows, as a waterlogged propagule's water does.
        var fluid = block.GetProperty("waterlogged") == "true" ? BlocksRegistry.Water : BlocksRegistry.Air;
        live.SetBlock(position, fluid);

        if (Place(features, tree, live, position, random))
            return true;

        live.SetBlock(position, block);
        return false;
    }

    // Places a tree, which fails rather than stopping the level when a plugin's feature throws.
    private static bool Place(
        WorldgenFeatureSet features,
        string identifier,
        LiveFeatureLevel level,
        Vector origin,
        IRandomSource random)
    {
        try
        {
            return features.PlaceConfigured(identifier, new FeatureContext
            {
                Level = level,
                Origin = origin,
                Random = random,
                Generation = level.Generation
            });
        }
        catch (PluginGenerationException exception)
        {
            Log.TreeFailed(level.Logger, exception, identifier);
            return false;
        }
    }

    // Vanilla's getConfiguredMegaFeature: the secondary mega tree's chance is only drawn when there's one.
    private static string? MegaTree(TreeGrower grower, IRandomSource random)
    {
        if (grower.SecondaryMegaTree is not null && random.NextFloat() < grower.SecondaryChance)
            return grower.SecondaryMegaTree;

        return grower.MegaTree;
    }

    // Vanilla's getConfiguredFeature: the secondary chance is always drawn, even for growers without a secondary
    // tree, since it decides the draws the tree itself gets.
    private static string? Tree(TreeGrower grower, IRandomSource random, bool flowers)
    {
        if (random.NextFloat() < grower.SecondaryChance)
        {
            if (flowers && grower.SecondaryFlowers is not null)
                return grower.SecondaryFlowers;

            if (grower.SecondaryTree is not null)
                return grower.SecondaryTree;
        }

        return flowers && grower.Flowers is not null ? grower.Flowers : grower.Tree;
    }

    // Vanilla's isTwoByTwoSapling: the square at the offset holds the same block, at any stage.
    private static bool IsTwoByTwo(LiveFeatureLevel level, IBlock block, Vector position, int dx, int dz)
    {
        var corner = position + new Vector(dx, 0, dz);

        return Same(corner)
            && Same(corner + Vector.East)
            && Same(corner + Vector.South)
            && Same(corner + new Vector(1, 0, 1));

        bool Same(Vector at) => level.GetBlock(at).RegistryId == block.RegistryId;
    }

    // Vanilla's hasFlowers: a flower from a block below to a block above, two blocks around.
    private static bool HasFlowers(LiveFeatureLevel level, Vector position)
    {
        for (var y = -1; y <= 1; y++)
        {
            for (var x = -2; x <= 2; x++)
            {
                for (var z = -2; z <= 2; z++)
                {
                    var block = level.GetBlock(position + new Vector(x, y, z));
                    if (TagsRegistry.Block.Flowers.Entries.Contains(block.RegistryId))
                        return true;
                }
            }
        }

        return false;
    }

    // Vanilla's CropBlock.growCrops: two to five ages (a third of that for beetroots, one for torchflowers), capped
    // at the crop's last age; a torchflower crop's last age is the torchflower.
    private static void GrowCrop(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var increase = block.RegistryId switch
        {
            BlockIds.TorchflowerCrop => 1,
            BlockIds.Beetroots => (2 + random.NextInt(4)) / 3,
            _ => 2 + random.NextInt(4)
        };

        var age = Math.Min(MaxAge(block), Age(block) + increase);
        SetAndShow(level, position, GrownState(block, age));
    }

    /// <summary>
    /// Vanilla's <c>GrassBlock.performBonemeal</c>: 128 tries, each walking a few random steps over grass blocks from
    /// above the block, that grow short grass into tall grass now and then and plant grass or one of the biome's
    /// flowers where there's air.
    /// </summary>
    private static void GrowGrass(AbstractLevel level, Vector position, IRandomSource random)
    {
        var features = PluginFeatures.CurrentRegistry.FeatureSet!;
        var live = new LiveFeatureLevel(level);
        var grass = features.FindPlaced(GrassBonemeal);
        var above = position + Vector.Up;

        try
        {
            for (var attempt = 0; attempt < 128; attempt++)
            {
                if (WalkOverGrass(live, above, attempt / 16, random) is not Vector target)
                    continue;

                var block = live.GetBlock(target);

                // The walk's draws, then these, in vanilla's order.
                if (block.Is(BlockIds.ShortGrass) && random.NextInt(10) == 0 && live.GetBlock(target + Vector.Up).IsAir)
                    PlaceDoublePlant(live, target, BlockIds.TallGrass);

                if (!block.IsAir)
                    continue;

                PlacedFeature? planted;
                if (random.NextInt(8) == 0)
                {
                    var flowers = FlowerFeatures(features, live.GetBiome(target));
                    if (flowers.Count == 0)
                        continue;

                    planted = flowers[random.NextInt(flowers.Count)].Feature;
                }
                else
                {
                    planted = grass;
                }

                if (planted is null)
                    continue;

                try
                {
                    features.PlacePlaced(planted, live, live.Generation, random, target);
                }
                catch (PluginGenerationException exception)
                {
                    Log.PlantFailed(live.Logger, exception);
                }
            }
        }
        finally
        {
            live.Commit();
        }
    }

    // A try's random steps: each moves up to a block sideways and maybe up or down, and ends the try when it leaves
    // the grass blocks or runs into a full block. The draws for x, then y (two), then z are vanilla's, in its order.
    private static Vector? WalkOverGrass(LiveFeatureLevel level, Vector start, int steps, IRandomSource random)
    {
        var position = start;

        for (var step = 0; step < steps; step++)
        {
            var dx = random.NextInt(3) - 1;
            var dy = (random.NextInt(3) - 1) * random.NextInt(3) / 2;
            var dz = random.NextInt(3) - 1;
            position += new Vector(dx, dy, dz);

            if (!level.GetBlock(position + Vector.Down).Is(BlockIds.GrassBlock)
                || level.GetBlock(position).IsCollisionShapeFullBlock())
            {
                return null;
            }
        }

        return position;
    }

    // Vanilla's BiomeGenerationSettings.getFlowerFeatures: the biome's flower features, in the order its steps list
    // them. Only features placed directly count, not ones nested in another feature, which vanilla's biomes don't do.
    private static List<FlowerFeature> FlowerFeatures(WorldgenFeatureSet features, BiomeCodec biome)
    {
        var flowers = new List<FlowerFeature>();
        if (!features.BiomeFeatures.TryGetValue(biome.Name, out var steps))
            return flowers;

        foreach (var step in steps)
        {
            foreach (var placed in step)
            {
                if (placed.Feature is FlowerFeature flower)
                    flowers.Add(flower);
            }
        }

        return flowers;
    }

    // Vanilla's TallGrassBlock.performBonemeal: short grass grows into tall grass and a fern into a large fern.
    private static void GrowTallGrass(AbstractLevel level, Vector position, IBlock block)
    {
        var live = new LiveFeatureLevel(level);
        PlaceDoublePlant(live, position, block.Is(BlockIds.Fern) ? BlockIds.LargeFern : BlockIds.TallGrass);
        live.Commit();
    }

    // Vanilla's DoublePlantBlock.placeAt: the plant's lower half here and its upper half above.
    private static void PlaceDoublePlant(LiveFeatureLevel level, Vector position, int plant)
    {
        var lower = BlocksRegistry.GetDefault(plant).WithProperty("half", "lower");

        level.SetBlock(position, lower);
        level.SetBlock(position + Vector.Up, lower.WithProperty("half", "upper"));
    }

    // Sets a block and shows it to the players, like vanilla's setBlock with UPDATE_CLIENTS.
    internal static void SetAndShow(AbstractLevel level, Vector position, IBlock block)
    {
        var live = new LiveFeatureLevel(level);
        live.SetBlock(position, block);
        live.Commit();
    }

    // Vanilla's getRawBrightness: the sky's light less the ambient darkening, or the blocks' light, whichever is more.
    // Saplings read it with the level's sky darkening (getMaxLocalRawBrightness); crops with none.
    internal static int RawBrightness(AbstractLevel level, Vector position, int ambientDarkness)
    {
        if (level.IsOutsideBuildHeight(position.Y)
            || level.GetLoadedChunk(position.X >> 4, position.Z >> 4) is not IChunk chunk)
        {
            return 0;
        }

        var sky = chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Sky) - ambientDarkness;
        var blocks = chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Block);

        return Math.Max(sky, blocks);
    }

    internal static IBlock BlockAt(AbstractLevel level, Vector position) =>
        level.IsOutsideBuildHeight(position.Y)
            ? BlocksRegistry.VoidAir
            : level.GetLoadedChunk(position.X >> 4, position.Z >> 4)?.GetBlock(position) ?? BlocksRegistry.Air;

    private static bool IsHanging(IBlock block) => block.GetProperty("hanging") == "true";

    internal static int Age(IBlock block) => int.TryParse(block.GetProperty("age"), out var age) ? age : 0;

    // A crop's last age: 7, or 3 for beetroots and 2 for torchflowers, whose crop's own states stop at 1.
    internal static int MaxAge(IBlock block) => block.RegistryId switch
    {
        BlockIds.Beetroots => 3,
        BlockIds.TorchflowerCrop => 2,
        _ => 7
    };

    // A crop at an age: vanilla's getStateForAge, where a torchflower's last age is the torchflower itself.
    internal static IBlock GrownState(IBlock block, int age) =>
        block.RegistryId == BlockIds.TorchflowerCrop && age == MaxAge(block)
            ? BlocksRegistry.GetDefault(BlockIds.Torchflower)
            : block.WithProperty("age", age);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "A sapling's tree {Feature} failed to grow.")]
        public static partial void TreeFailed(ILogger logger, Exception exception, string feature);

        [LoggerMessage(Level = LogLevel.Warning, Message = "A plant bone meal grows failed to grow.")]
        public static partial void PlantFailed(ILogger logger, Exception exception);
    }
}
