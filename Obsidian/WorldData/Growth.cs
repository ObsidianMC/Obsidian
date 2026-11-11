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
/// crops, grass blocks and short grass and ferns here, and the other plants bone meal grows in
/// <c>Growth.Plants.cs</c>. Checked against the <c>growing_plant_ticks</c> capture.
/// </summary>
/// <remarks>
/// Growing a plant draws from the random it's given in vanilla's order (the chance, then which tree, then the tree's
/// own draws), and places the features vanilla places, found by identifier like vanilla's registry lookups, so that
/// plugins' and data packs' overrides apply (see <see cref="GrowthFeatures"/>). Call it on the level's tick, holding
/// the level's <see cref="Fluids.LevelFluids.TickLock"/> from reading the block to growing it, which places the
/// features in the level's chunks (see <see cref="LiveFeatureLevel"/>).
/// </remarks>
internal static partial class Growth
{
    // Vanilla's LevelEvent.PARTICLES_AND_SOUND_PLANT_GROWTH, which bone meal sends with 15 particles.
    public const int BoneMealEvent = 1505;

    public const int BoneMealParticles = 15;

    // The light a sapling needs above it to grow (vanilla's SaplingBlock.randomTick).
    private const int SaplingLight = 9;

    private const string GrassBonemeal = "minecraft:grass_bonemeal";

    // Vanilla's setBlock flags for a sapling turning to stage 1, or put back where its tree failed (260:
    // UPDATE_INVISIBLE | UPDATE_SKIP_BLOCK_ENTITY_SIDEEFFECTS), and for clearing it for its tree (818: UPDATE_CLIENTS
    // | UPDATE_KNOWN_SHAPE | UPDATE_SUPPRESS_DROPS | UPDATE_SKIP_BLOCK_ENTITY_SIDEEFFECTS | UPDATE_SKIP_ON_PLACE).
    // BlockUpdateFlags shares vanilla's bits, though it doesn't name all of these.
    private const BlockUpdateFlags SaplingResetFlags = (BlockUpdateFlags)260;

    private const BlockUpdateFlags SaplingRemovalFlags = (BlockUpdateFlags)818;

    // The tree of vanilla's azalea TreeGrower (AzaleaBlock grows TreeGrower.AZALEA, which no sapling holds).
    private const string AzaleaTree = "minecraft:azalea_tree";

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

    private static readonly TreeGrower azalea = new()
    {
        Trees = [new WeightedFeature(AzaleaTree, 1)],
        ShortestTree = AzaleaTree
    };

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
                var aged = block.WithProperty("age", Age(block) + 1);
                level.BlockUpdates.SetBlock(position, aged, BlockUpdateFlags.Clients);
                break;
        }
    }

    /// <summary>
    /// Whether bone meal does something to <paramref name="block"/>, which uses it up (vanilla's
    /// <c>isValidBonemealTarget</c>).
    /// </summary>
    /// <param name="random">
    /// The level's random, when this is the check that decides the use, which a sapling draws from as vanilla's
    /// does; null for an early check, which draws nothing (the draws never change the answer).
    /// </param>
    public static bool IsValidBonemealTarget(AbstractLevel level, Vector position, IBlock block,
        IRandomSource? random = null) =>
        KindOf(block, out var grower) switch
        {
            Kind.Sapling => SaplingTakesBonemeal(level, position, block, grower!, random),
            Kind.Propagule => !IsHanging(block) || Age(block) < 4,
            Kind.Azalea => AzaleaTakesBonemeal(level, position),
            Kind.Crop => Age(block) < MaxAge(block),
            Kind.GrassBlock => BlockAt(level, position + Vector.Up).IsAir,
            Kind.TallGrass => BlockAt(level, position + Vector.Up).IsAir,
            _ => PlantFor(block) is BonemealPlant plant && plant.IsValidTarget(level, position, block)
        };

    /// <summary>
    /// Bone meal on a block that takes it (see <see cref="IsValidBonemealTarget"/>): it grows when its chance comes
    /// up, like vanilla's <c>BoneMealItem.growCrop</c>.
    /// </summary>
    public static void ApplyBonemeal(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        var kind = KindOf(block, out var grower);
        if (kind == Kind.None)
        {
            if (PlantFor(block) is BonemealPlant plant && plant.IsSuccess(random))
                plant.Grow(level, position, block, random);

            return;
        }

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
                level.BlockUpdates.SetBlock(position, block.WithProperty("age", Age(block) + 1),
                    BlockUpdateFlags.Clients);
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
            level.BlockUpdates.SetBlock(position, block.WithProperty("stage", 1), SaplingResetFlags);
            return;
        }

        GrowTree(level, position, block, grower, random);
    }

    /// <summary>
    /// Vanilla 26.3's <c>TreeGrower.growTree</c>: four saplings in a square grow a mega tree from their north-west
    /// corner, else the sapling grows a tree, or a flowers' tree with a flower nearby, each picked by weight. The
    /// saplings make way for the tree, and come back when it can't grow.
    /// </summary>
    /// <remarks>
    /// The draws are vanilla's, in its order: the mega tree's pick whenever the grower has mega trees, square or not,
    /// then the tree's, then the tree's own. A tree that reaches a chunk that isn't loaded and complete doesn't grow,
    /// rather than grow cut off there: vanilla would have loaded the chunk.
    /// </remarks>
    /// <returns>Whether a tree grew.</returns>
    private static bool GrowTree(
        AbstractLevel level,
        Vector position,
        IBlock block,
        TreeGrower grower,
        IRandomSource random)
    {
        var lists = Lists(grower);
        var features = level.GrowthFeatures;

        if (Pick(lists.Mega, random) is string mega
            && features.Find(mega) is not null
            && FindSquare(level, block, position) is Vector corner)
        {
            Vector[] square =
            [
                corner,
                corner + Vector.East,
                corner + Vector.South,
                corner + new Vector(1, 0, 1)
            ];

            // Each sapling comes back as it was, stage and all.
            var saplings = Array.ConvertAll(square, sapling => BlockAt(level, sapling));
            foreach (var sapling in square)
                RemoveSapling(level, sapling);

            if (PlaceFeature(level, mega, corner, random))
                return true;

            for (var index = 0; index < square.Length; index++)
                level.BlockUpdates.SetBlock(square[index], saplings[index], SaplingResetFlags);

            return false;
        }

        var tree = Pick(TreeList(lists, HasFlowers(level, position)), random);
        if (tree is null || features.Find(tree) is null)
            return false;

        RemoveSapling(level, position);
        if (PlaceFeature(level, tree, position, random))
            return true;

        level.BlockUpdates.SetBlock(position, block, SaplingResetFlags);
        return false;
    }

    /// <summary>
    /// Places a configured feature a plant grows into, through a <see cref="LiveFeatureLevel"/> on the level. One that
    /// reaches a chunk that isn't loaded and complete is taken back and counts as not placed.
    /// </summary>
    internal static bool PlaceFeature(AbstractLevel level, string identifier, Vector origin, IRandomSource random)
    {
        var live = new LiveFeatureLevel(level);
        var placed = level.GrowthFeatures.Place(identifier, live, origin, random);

        if (live.ReachedUnloaded)
        {
            live.Rollback();
            placed = false;
        }

        live.Commit();
        return placed;
    }

    // Vanilla's removeSapling: the sapling's fluid stays where the tree grows, as a waterlogged propagule's water does.
    private static void RemoveSapling(AbstractLevel level, Vector position)
    {
        var sapling = BlockAt(level, position);
        var fluid = sapling.GetProperty("waterlogged") == "true" ? BlocksRegistry.Water : BlocksRegistry.Air;
        level.BlockUpdates.SetBlock(position, fluid, SaplingRemovalFlags);
    }

    // Vanilla's SaplingBlock.isValidBonemealTarget: TreeGrower.canGrow, then room for the shortest tree's trunk.
    // canGrow picks a tree, then a mega tree, from the level's random; only whether the lists are empty decides it,
    // but the draws are vanilla's and the bone meal's chance comes after them.
    private static bool SaplingTakesBonemeal(
        AbstractLevel level,
        Vector position,
        IBlock block,
        TreeGrower grower,
        IRandomSource? random)
    {
        var lists = Lists(grower);
        var tree = Pick(TreeList(lists, HasFlowers(level, position)), random);
        var mega = Pick(lists.Mega, random);
        if (tree is null && mega is not null && FindSquare(level, block, position) is null)
            return false;

        return !level.IsOutsideBuildHeight(position.Y + MinimumHeight(level, grower));
    }

    // Vanilla's AzaleaBlock.isValidBonemealTarget: room for the azalea tree's trunk and two more, and no fluid above.
    private static bool AzaleaTakesBonemeal(AbstractLevel level, Vector position)
    {
        var top = position.Y + MinimumHeight(level, azalea) + 2;
        return !level.IsOutsideBuildHeight(top) && !BlockAt(level, position + Vector.Up).HasFluid();
    }

    // Vanilla's TreeGrower.getMinimumHeight: the shortest tree's trunk base height, or 0 when it isn't a tree.
    private static int MinimumHeight(AbstractLevel level, TreeGrower grower) =>
        grower.ShortestTree is string shortest ? level.GrowthFeatures.TrunkBaseHeight(shortest) ?? 0 : 0;

    // A grower's weighted lists: its own, or the older shape's (see TreeGrower) when it has none.
    private static GrowerLists Lists(TreeGrower grower)
    {
        if (grower.Trees.Count > 0 || grower.MegaTrees.Count > 0 || grower.FlowerTrees.Count > 0)
            return new GrowerLists(grower.Trees, grower.MegaTrees, grower.FlowerTrees);

        var chance = Math.Clamp(grower.SecondaryChance, 0f, 1f);
        return new GrowerLists(
            OlderShape(grower.Tree, grower.SecondaryTree, chance),
            OlderShape(grower.MegaTree, grower.SecondaryMegaTree, chance),
            OlderShape(grower.Flowers, grower.SecondaryFlowers, chance));
    }

    // A feature and its secondary as a weighted list: the secondary takes the chance's share of a thousand, and a
    // feature alone weighs 1, like vanilla's one-tree lists.
    private static IReadOnlyList<WeightedFeature> OlderShape(string? feature, string? secondary, float chance)
    {
        const int Total = 1000;
        var secondaryWeight = secondary is null ? 0 : (int)MathF.Round(chance * Total);
        if (secondaryWeight == 0)
            return feature is null ? [] : [new WeightedFeature(feature, 1)];

        var list = new List<WeightedFeature>();
        if (feature is not null && secondaryWeight < Total)
            list.Add(new WeightedFeature(feature, Total - secondaryWeight));

        list.Add(new WeightedFeature(secondary!, secondaryWeight));
        return list;
    }

    // Vanilla's getConfiguredFeature: the flowers' trees near flowers, when the grower has any.
    private static IReadOnlyList<WeightedFeature> TreeList(GrowerLists lists, bool flowers) =>
        flowers && lists.Flowers.Count > 0 ? lists.Flowers : lists.Trees;

    /// <summary>
    /// Vanilla's <c>WeightedList.getRandom</c>: one <c>nextInt</c> of the total weight, which picks the entry it falls
    /// in, in the list's order. An empty list picks nothing and draws nothing.
    /// </summary>
    /// <param name="list">The weighted features.</param>
    /// <param name="random">The random to draw from, or null to pick the first entry without drawing.</param>
    internal static string? Pick(IReadOnlyList<WeightedFeature> list, IRandomSource? random)
    {
        var total = 0;
        foreach (var entry in list)
            total += entry.Weight;

        if (total <= 0)
            return null;

        if (random is null)
            return list[0].Feature;

        var selection = random.NextInt(total);
        foreach (var entry in list)
        {
            selection -= entry.Weight;
            if (selection < 0)
                return entry.Feature;
        }

        return null;
    }

    // Vanilla's findTwoByTwoSaplingPos: the north-west corner of a square of the same block (any stage) holding the
    // sapling, tried from the sapling's own corner outwards.
    private static Vector? FindSquare(AbstractLevel level, IBlock block, Vector position)
    {
        for (var dx = 0; dx >= -1; dx--)
        {
            for (var dz = 0; dz >= -1; dz--)
            {
                var corner = position + new Vector(dx, 0, dz);
                if (Same(corner)
                    && Same(corner + Vector.East)
                    && Same(corner + Vector.South)
                    && Same(corner + new Vector(1, 0, 1)))
                {
                    return corner;
                }
            }
        }

        return null;

        bool Same(Vector at) => BlockAt(level, at).RegistryId == block.RegistryId;
    }

    // Vanilla's hasFlowers: a flower from a block below to a block above, two blocks around.
    private static bool HasFlowers(AbstractLevel level, Vector position)
    {
        for (var y = -1; y <= 1; y++)
        {
            for (var x = -2; x <= 2; x++)
            {
                for (var z = -2; z <= 2; z++)
                {
                    var block = BlockAt(level, position + new Vector(x, y, z));
                    if (TagsRegistry.Block.Flowers.Entries.Contains(block.RegistryId))
                        return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// A grower's three weighted lists, as vanilla's <c>TreeGrower</c> holds them.
    /// </summary>
    private readonly record struct GrowerLists(
        IReadOnlyList<WeightedFeature> Trees,
        IReadOnlyList<WeightedFeature> Mega,
        IReadOnlyList<WeightedFeature> Flowers);

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
        [LoggerMessage(Level = LogLevel.Warning, Message = "A plant bone meal grows failed to grow.")]
        public static partial void PlantFailed(ILogger logger, Exception exception);
    }
}
