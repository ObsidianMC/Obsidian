using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Terrain;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Features;

/// <summary>Places a 26.3 feature at a position, returning whether it placed anything.</summary>
internal delegate bool Feature(DecorationContext context, IRandomSource random, Vector origin);

/// <summary>
/// What features and placement modifiers see of the chunk being decorated: the level (a features-step region), the
/// biome at a block position and the generator's build range, which height providers resolve anchors against.
/// </summary>
internal sealed record DecorationContext(
    FeatureRegistry Registry,
    IWorldGenLevel Level,
    Func<Vector, string> BiomeAt,
    int GeneratorMinY,
    int GeneratorHeight);

/// <summary>
/// Compiles 26.3's <c>feature</c> and <c>placed_feature</c> registries (config at the root, not under <c>config</c>),
/// their placement modifiers, block predicates, rule tests and block-state providers, beside the legacy 1.21.11
/// features the live generator still uses (Phase 5 of <c>docs/porting-26.3-plan.md</c>).
/// </summary>
/// <remarks>
/// Entries compile on first use and are cached by identifier; the biome filter compares registered placed features
/// by identifier, like vanilla compares holders, without compiling the biome's other features. Compiling isn't
/// thread-safe; compile what a generator needs before sharing the registry. Types that aren't implemented yet throw
/// <see cref="NotSupportedException"/> when compiled. Per-biome decoration (each biome's step lists and the feature
/// index order across biomes) waits for 26.3 biome selection.
/// </remarks>
internal sealed partial class FeatureRegistry(WorldgenData data)
{
    private readonly Dictionary<string, PlacedFeature> placedFeatures = [];
    private readonly Dictionary<string, Feature> features = [];
    private readonly Dictionary<string, HashSet<int>> blockTags = [];
    private readonly Dictionary<string, HashSet<string>> biomeFeatures = [];

    public WorldgenData Data { get; } = data;

    /// <summary>A placed feature: a reference into the <c>placed_feature</c> registry or an inline one.</summary>
    public PlacedFeature Placed(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.String)
        {
            return this.CompilePlaced(json, id: null);
        }

        var id = json.GetString()!;

        if (!this.placedFeatures.TryGetValue(id, out var placed))
        {
            placed = this.CompilePlaced(this.Data.Read("placed_feature", id), id);
            this.placedFeatures.Add(id, placed);
        }

        return placed;
    }

    public PlacedFeature Placed(string id) => this.Placed(JsonSerializer.SerializeToElement(id));

    /// <summary>A feature: a reference into the <c>feature</c> registry or an inline one.</summary>
    public Feature Feature(JsonElement json)
    {
        if (json.ValueKind != JsonValueKind.String)
        {
            return this.CompileFeature(json);
        }

        var id = json.GetString()!;

        if (!this.features.TryGetValue(id, out var feature))
        {
            feature = this.CompileFeature(this.Data.Read("feature", id));
            this.features.Add(id, feature);
        }

        return feature;
    }

    /// <summary>The registry ids of a block tag's blocks, including nested tags.</summary>
    public HashSet<int> BlockTag(string id)
    {
        if (this.blockTags.TryGetValue(id, out var blocks))
        {
            return blocks;
        }

        blocks = [];

        foreach (var entry in this.Data.Read("block_tag", id).GetProperty("values").EnumerateArray())
        {
            var name = entry.ValueKind == JsonValueKind.String
                ? entry.GetString()!
                : entry.GetProperty("id").GetString()!;

            if (name.StartsWith('#'))
            {
                blocks.UnionWith(this.BlockTag(name[1..]));
            }
            else
            {
                blocks.Add(BlocksRegistry.Get(name).RegistryId);
            }
        }

        this.blockTags.Add(id, blocks);
        return blocks;
    }

    /// <summary>Whether any decoration step of the biome lists the placed feature (vanilla's biome filter).</summary>
    public bool BiomeHasFeature(string biome, PlacedFeature feature)
    {
        if (!this.biomeFeatures.TryGetValue(biome, out var placed))
        {
            placed = [];

            foreach (var step in this.Data.Read("biome", biome).GetProperty("features").EnumerateArray())
            {
                foreach (var entry in step.EnumerateArray())
                {
                    placed.Add(entry.GetString()!);
                }
            }

            this.biomeFeatures.Add(biome, placed);
        }

        return feature.Id is not null && placed.Contains(feature.Id);
    }

    internal static IBlock ReadBlock(JsonElement json) => MaterialRules.ReadBlock(json);

    private PlacedFeature CompilePlaced(JsonElement json, string? id)
    {
        var feature = this.Feature(json.GetProperty("feature"));
        var modifiers = json.GetProperty("placement").EnumerateArray().Select(this.Modifier).ToArray();

        return new PlacedFeature(id, feature, modifiers);
    }

    private Feature CompileFeature(JsonElement json)
    {
        var type = json.GetProperty("type").GetString();

        return type switch
        {
            "minecraft:simple_block" => this.SimpleBlock(json),
            "minecraft:random_selector" => this.RandomSelector(json),
            "minecraft:simple_random_selector" => this.SimpleRandomSelector(json),
            "minecraft:random_boolean_selector" => this.RandomBooleanSelector(json),
            "minecraft:weighted_random_selector" => this.WeightedRandomSelector(json),
            "minecraft:sequence" => this.Sequence(json),
            "minecraft:overlay" => this.Overlay(json),
            "minecraft:random_neighbor_spread" => this.RandomNeighborSpread(json),
            "minecraft:lake" => this.Lake(json),
            "minecraft:vegetation_patch" => this.VegetationPatch(json, waterlogged: false),
            "minecraft:waterlogged_vegetation_patch" => this.VegetationPatch(json, waterlogged: true),
            "minecraft:speleothem" => this.Speleothem(json),
            "minecraft:speleothem_cluster" => this.SpeleothemCluster(json),
            "minecraft:multiface_growth" => this.MultifaceGrowth(json),
            "minecraft:netherrack_replace_blobs" => ReplaceBlobs(json),
            "minecraft:end_island" => EndIsland(),
            "minecraft:delta_feature" => Delta(json),
            "minecraft:chorus_plant" => this.ChorusPlant(),
            "minecraft:end_gateway" => EndGateway(json),
            "minecraft:single_block_pillar" => this.Pillar(json),
            "minecraft:projected_random_patchy_square" => this.ProjectedPatchySquare(json),
            "minecraft:stepped_column_cluster" => this.SteppedColumns(json),
            "minecraft:template" => this.TemplateFeature(json),
            "minecraft:bamboo" => Bamboo(json),
            "minecraft:blue_ice" => BlueIce(),
            "minecraft:bonus_chest" => BonusChest(),
            "minecraft:monster_room" => MonsterRoom(),
            "minecraft:end_spike" => EndSpike(json),
            "minecraft:end_platform" => EndPlatform(),
            "minecraft:vines" => Vines(),
            "minecraft:void_start_platform" => VoidStartPlatform(),
            "minecraft:block_pile" => this.BlockPile(json),
            "minecraft:freeze_top_layer" => FreezeSurface(),
            "minecraft:coral_tree" => this.CoralTree(json),
            "minecraft:coral_claw" => this.CoralClaw(json),
            "minecraft:no_op" => static (_, _, _) => true,
            "minecraft:root_system" => this.RootSystem(json),
            "minecraft:ore" => this.Ore(json, scattered: false),
            "minecraft:scattered_ore" => this.Ore(json, scattered: true),
            "minecraft:block_column" => this.BlockColumn(json),
            "minecraft:disk" => this.Disk(json),
            "minecraft:block_blob" => this.BlockBlob(json),
            "minecraft:spring_feature" => this.Spring(json),
            "minecraft:tree" => this.Tree(json),
            "minecraft:fallen_tree" => this.FallenTree(json),
            "minecraft:spike" => this.Spike(json),
            "minecraft:huge_red_mushroom" => this.HugeMushroom(json, red: true),
            "minecraft:huge_brown_mushroom" => this.HugeMushroom(json, red: false),
            "minecraft:huge_fungus" => this.HugeFungus(json),
            "minecraft:end_podium" => EndPodium(json),
            "minecraft:fossil" => this.Fossil(json),
            "minecraft:geode" => this.Geode(json),
            "minecraft:iceberg" => Iceberg(json),
            "minecraft:large_dripstone" => this.LargeDripstone(json),
            "minecraft:sculk_patch" => this.SculkPatch(json),
            "minecraft:underwater_magma" => UnderwaterMagma(json),
            _ => throw new NotSupportedException($"The 26.3 feature {type} isn't implemented yet.")
        };
    }
}

/// <summary>
/// A feature and its placement modifiers. Like vanilla's FeaturePlacer, each modifier runs to completion on one
/// position (drawing its randoms) before the next modifier sees the positions it produced, depth first, and every
/// final position places the feature.
/// </summary>
internal sealed class PlacedFeature(string? id, Feature feature, PlacementModifier[] modifiers)
{
    /// <summary>The registered identifier, or null for an inline placed feature.</summary>
    public string? Id { get; } = id;

    public Feature Feature { get; } = feature;

    public bool Place(DecorationContext context, IRandomSource random, Vector origin) =>
        this.Place(context, random, origin, top: null);

    /// <summary>Places the feature as a biome's decoration does, so a biome modifier can check the biome.</summary>
    public bool PlaceWithBiomeCheck(DecorationContext context, IRandomSource random, Vector origin) =>
        this.Place(context, random, origin, top: this);

    private bool Place(DecorationContext context, IRandomSource random, Vector origin, PlacedFeature? top)
    {
        if (modifiers.Length == 0)
        {
            return this.Feature(context, random, origin);
        }

        var placement = new PlacementContext(context, top);
        return this.Modify(placement, random, origin, 0);
    }

    private bool Modify(PlacementContext placement, IRandomSource random, Vector position, int index)
    {
        var positions = new List<Vector>();
        modifiers[index](placement, random, position, positions);

        var placed = false;
        var last = index + 1 == modifiers.Length;

        foreach (var next in positions)
        {
            var result = last
                ? this.Feature(placement.Decoration, random, next)
                : this.Modify(placement, random, next, index + 1);
            placed |= result;
        }

        return placed;
    }
}
