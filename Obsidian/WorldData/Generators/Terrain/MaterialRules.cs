using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using System.IO;
using System.Text.Json;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>
/// Compiles 26.3 material rules and conditions from their separate registries. A rule returns null to let the next
/// rule run; ore veins participate in that same sequence, after fluids have been chosen and before surface materials.
/// </summary>
internal sealed partial class MaterialRules
{
    private readonly TerrainGenerator generator;
    private readonly Dictionary<string, Func<MaterialContext, IBlock?>> rules = [];
    private readonly Dictionary<string, Func<MaterialContext, bool>> conditions = [];
    private readonly Func<MaterialContext, IBlock?> root;
    private readonly IBlock[] bands;

    // The biomes biome conditions name, numbered, so that a block looks its biome's name up once rather than once
    // per condition (see MaterialContext.BiomeId).
    private readonly ConcurrentDictionary<string, int> biomeIds = new();

    // The noises and densities rules sample, numbered for the arrays a context caches their samples in. Like the
    // rule caches above, they're filled while compiling, which the constructor does for the settings' rule.
    private readonly Dictionary<NoiseStack, int> noiseSlots = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<DensityFunction, int> densitySlots = new(ReferenceEqualityComparer.Instance);

    public MaterialRules(TerrainGenerator generator)
    {
        this.generator = generator;
        this.bands = GenerateBands(generator.Compiler.Random.FromHashOf("minecraft:clay_bands"));
        this.root = this.CompileRule(generator.Settings.MaterialRule);
    }

    private NoiseStack SurfaceNoise => field ??= this.generator.Compiler.Noise("minecraft:surface");

    // Stone depth conditions with a secondary range read it; it is given its slot when the first is compiled.
    private NoiseStack SecondaryNoise => field ??= this.generator.Compiler.Noise("minecraft:surface_secondary");

    private int SecondarySlot { get; set; } = -1;

    private int NoiseSlot(NoiseStack noise)
    {
        if (!this.noiseSlots.TryGetValue(noise, out var slot))
        {
            slot = this.noiseSlots.Count;
            this.noiseSlots.Add(noise, slot);
        }

        return slot;
    }

    private int DensitySlot(DensityFunction function)
    {
        if (!this.densitySlots.TryGetValue(function, out var slot))
        {
            slot = this.densitySlots.Count;
            this.densitySlots.Add(function, slot);
        }

        return slot;
    }

    /// <summary>The biome condition numbers (see <see cref="BiomeId"/>) of <paramref name="names"/>.</summary>
    private bool[] ReachableIds(IReadOnlyList<string> names)
    {
        var reachable = new bool[this.biomeIds.Count];

        foreach (var name in names)
        {
            var id = this.FindBiomeId(name);

            if (id >= 0)
                reachable[id] = true;
        }

        return reachable;
    }

    public void Apply(TerrainContext terrain)
    {
        var biomeAt = terrain.Biomes.Surface;
        using var context = new MaterialContext(this, terrain, biomeAt, biomeNames: terrain.Biomes.SurfaceNames);
        var chunk = terrain.Chunk;
        var kinds = new BlockKind[chunk.Height + 1];

        // Each column sees the heightmaps updated by earlier columns, including badlands and iceberg extensions.
        // This is vanilla's x-major surface traversal, distinct from the density buffer's z-major order.
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                var startY = SurfaceHeight(chunk, x, z);
                var biome = biomeAt(terrain.Volume.BlockX(x), startY, terrain.Volume.BlockZ(z));
                context.UpdateColumn(x, z);

                if (biome == "minecraft:eroded_badlands")
                {
                    this.ExtendBadlands(context, startY);
                }

                this.ApplyColumn(context, SurfaceHeight(chunk, x, z), kinds);

                if (biome is "minecraft:frozen_ocean" or "minecraft:deep_frozen_ocean")
                {
                    this.ExtendIceberg(context, biome, startY);
                }
            }
        }
    }

    // The column's blocks are read once, before any rule replaces one: a block is replaced after the blocks below it
    // were read, and the stone depth search only reads below. Kinds are indexed from one below the chunk, which reads
    // as air like any block outside it.
    private void ApplyColumn(MaterialContext context, int startY, BlockKind[] kinds)
    {
        var above = 0;
        var water = int.MinValue;
        var bottom = int.MaxValue;
        var chunk = context.Terrain.Chunk;
        var minY = chunk.MinY;
        var top = Math.Min(startY, minY + chunk.Height - 1);

        ReadKinds(chunk, context.LocalX, context.LocalZ, top, kinds);

        for (var y = startY; y >= minY; y--)
        {
            var kind = y > top ? BlockKind.Air : kinds[y - minY + 1];

            if (kind == BlockKind.Air)
            {
                above = 0;
                water = int.MinValue;
                continue;
            }

            if (kind == BlockKind.Liquid)
            {
                if (water == int.MinValue)
                {
                    water = y + 1;
                }

                continue;
            }

            if (bottom >= y)
            {
                bottom = -32512;

                for (var belowY = y - 1; belowY >= minY - 1; belowY--)
                {
                    if (kinds[belowY - minY + 1] != BlockKind.Solid)
                    {
                        bottom = belowY + 1;
                        break;
                    }
                }
            }

            above++;
            context.UpdateBlock(y, above, y - bottom + 1, water);
            var replacement = this.root(context);

            if (replacement is not null)
            {
                context.SetBlock(y, replacement);
            }
        }
    }

    /// <summary>
    /// A freshly exposed surface during carving uses depth one, a one-block density volume and the carving biomes.
    /// </summary>
    public IBlock? TopMaterial(TerrainContext terrain, int x, int y, int z, bool underwater)
    {
        var volume = new DensityVolume(1, 1, 1, x, y, z);
        using var context = new MaterialContext(this, terrain, terrain.Biomes.Carving, volume);
        context.UpdateColumn(x & 15, z & 15);
        context.UpdateBlock(y, 1, 1, underwater ? y + 1 : int.MinValue);

        return this.root(context);
    }

    public Func<MaterialContext, IBlock?> CompileRule(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            var id = WorldgenData.Qualify(json.GetString()!);

            if (!this.rules.TryGetValue(id, out var rule))
            {
                rule = this.CompileRule(this.generator.Data.Read("material_rule", id));
                this.rules.Add(id, rule);
            }

            return rule;
        }

        switch (json.GetProperty("type").GetString())
        {
            case "minecraft:block":
                var block = ReadBlock(json.GetProperty("result_state"));
                return _ => block;
            case "minecraft:sequence":
                var sequence = json.GetProperty("sequence").EnumerateArray().Select(this.CompileRule).ToArray();
                return context =>
                {
                    foreach (var rule in sequence)
                    {
                        var result = rule(context);

                        if (result is not null)
                        {
                            return result;
                        }
                    }

                    return null;
                };
            case "minecraft:condition":
                var condition = this.CompileCondition(json.GetProperty("if_true"));
                var then = this.CompileRule(json.GetProperty("then_run"));
                return context => condition(context) ? then(context) : null;
            case "minecraft:bandlands":
                var offsetNoise = this.generator.Compiler.Noise("minecraft:clay_bands_offset");
                return context =>
                {
                    var value = offsetNoise.Get(context.X, 0.0, context.Z) * 4.0;
                    var offset = (int)Math.Floor(value + 0.5);
                    var index = FloatMath.FloorMod(context.Y + offset + this.bands.Length, this.bands.Length);

                    return this.bands[index];
                };
            case "minecraft:ore_vein":
                return this.CompileOre(json);
            default:
                throw new InvalidDataException($"Unknown 26.3 material rule {json.GetProperty("type")}.");
        }
    }

    private Func<MaterialContext, IBlock?> CompileOre(JsonElement json)
    {
        var compiler = this.generator.Compiler;
        var density = compiler.Compile(json.GetProperty("density"), "ore_vein.density");
        var richness = compiler.Compile(json.GetProperty("richness"), "ore_vein.richness");
        var gap = compiler.Compile(json.GetProperty("filler_gap"), "ore_vein.filler_gap");
        var densitySlot = this.DensitySlot(density);
        var richnessSlot = this.DensitySlot(richness);
        var ore = ReadBlock(json.GetProperty("ore_block"));
        var raw = ReadBlock(json.GetProperty("raw_ore_block"));
        var filler = ReadBlock(json.GetProperty("filler_block"));
        var rawChance = json.GetProperty("raw_ore_chance").GetSingle();
        var factory = compiler.RandomFactory("minecraft:ore");

        return context =>
        {
            var chance = context.Density(densitySlot, density);

            if (chance <= 0.0f)
            {
                return null;
            }

            // Matches 26.3's ore rule: draw density acceptance, richness acceptance, then raw ore only after a gap
            // accepts the ore. The two ore types start the same positional sequence independently.
            var random = new PositionalRandom(factory, context.X, context.Y, context.Z);

            if (random.NextFloat() > chance)
            {
                return null;
            }

            var oreChance = context.Density(richnessSlot, richness);

            if (random.NextFloat() < oreChance && context.PointDensity(gap) < 0.0f)
            {
                return random.NextFloat() < rawChance ? raw : ore;
            }

            return filler;
        };
    }

    public Func<MaterialContext, bool> CompileCondition(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            var id = WorldgenData.Qualify(json.GetString()!);

            if (!this.conditions.TryGetValue(id, out var condition))
            {
                condition = this.CompileCondition(this.generator.Data.Read("material_condition", id));
                this.conditions.Add(id, condition);
            }

            return condition;
        }

        switch (json.GetProperty("type").GetString())
        {
            case "minecraft:biome":
                var names = json.GetProperty("biome_is");
                var biomes = names.ValueKind == JsonValueKind.String
                    ? [WorldgenData.Qualify(names.GetString()!)]
                    : names.EnumerateArray().Select(name => WorldgenData.Qualify(name.GetString()!)).ToArray();
                var ids = biomes.Select(this.BiomeId).ToArray();
                return context => context.IsBiome(ids);
            case "minecraft:not":
                var inverted = this.CompileCondition(json.GetProperty("invert"));
                return context => !inverted(context);
            case "minecraft:noise_threshold":
                var noiseId = WorldgenData.Qualify(json.GetProperty("noise").GetString()!);
                var noise = this.generator.Compiler.Noise(noiseId);
                var slot = this.NoiseSlot(noise);
                var min = json.GetProperty("min_threshold").GetDouble();
                var max = json.GetProperty("max_threshold").GetDouble();
                var is3d = json.TryGetProperty("is_3d", out var is3dJson) && is3dJson.GetBoolean();
                return context =>
                {
                    var value = context.Noise(slot, noise, is3d);
                    return value >= min && value <= max;
                };
            case "minecraft:vertical_gradient":
                return this.CompileGradient(json);
            case "minecraft:stone_depth":
                return this.CompileStoneDepth(json);
            case "minecraft:water":
                var waterOffset = json.GetProperty("offset").GetInt32();
                var waterMultiplier = json.GetProperty("surface_depth_multiplier").GetInt32();
                var waterDepth = json.GetProperty("add_stone_depth").GetBoolean();
                return context => context.Water == int.MinValue
                    || context.Y + (waterDepth ? context.Above : 0)
                        >= context.Water + waterOffset + context.SurfaceDepth * waterMultiplier;
            case "minecraft:y_above":
                var anchor = this.ResolveAnchor(json.GetProperty("anchor"));
                var yMultiplier = json.GetProperty("surface_depth_multiplier").GetInt32();
                var yDepth = json.GetProperty("add_stone_depth").GetBoolean();
                return context => context.Y + (yDepth ? context.Above : 0)
                    >= anchor + context.SurfaceDepth * yMultiplier;
            case "minecraft:above_preliminary_surface":
                return context => context.Y >= context.MinSurface;
            case "minecraft:hole":
                return context => context.SurfaceDepth <= 0;
            case "minecraft:steep":
                return context => context.GradientX <= -4 || context.GradientZ >= 4;
            case "minecraft:temperature":
                return context => this.Temperature(context.Biome, context.X, context.Y, context.Z) < 0.15f;
            default:
                throw new InvalidDataException($"Unknown 26.3 material condition {json.GetProperty("type")}.");
        }
    }

    private int BiomeId(string biome) => this.biomeIds.GetOrAdd(biome, static (_, ids) => ids.Count, this.biomeIds);

    /// <summary>The number biome conditions know a biome by, or -1 for a biome none of them names.</summary>
    private int FindBiomeId(string biome) => this.biomeIds.TryGetValue(biome, out var id) ? id : -1;

    private Func<MaterialContext, bool> CompileGradient(JsonElement json)
    {
        var below = this.ResolveAnchor(json.GetProperty("true_at_and_below"));
        var above = this.ResolveAnchor(json.GetProperty("false_at_and_above"));
        var name = WorldgenData.Qualify(json.GetProperty("random_name").GetString()!);
        var factory = this.generator.Compiler.RandomFactory(name);

        return context =>
        {
            if (context.Y <= below)
            {
                return true;
            }

            if (context.Y >= above)
            {
                return false;
            }

            var chance = 1.0 + (double)(context.Y - below) / (above - below) * -1.0;
            var random = new PositionalRandom(factory, context.X, context.Y, context.Z);

            return random.NextFloat() < chance;
        };
    }

    private Func<MaterialContext, bool> CompileStoneDepth(JsonElement json)
    {
        var ceiling = json.GetProperty("surface_type").GetString() == "ceiling";
        var offset = json.GetProperty("offset").GetInt32();
        var addSurface = json.GetProperty("add_surface_depth").GetBoolean();
        var secondaryRange = json.GetProperty("secondary_depth_range").GetInt32();

        if (secondaryRange != 0 && this.SecondarySlot < 0)
        {
            this.SecondarySlot = this.NoiseSlot(this.SecondaryNoise);
        }

        return context =>
        {
            var depth = ceiling ? context.Below : context.Above;
            var surface = addSurface ? context.SurfaceDepth : 0;
            var secondary = secondaryRange == 0 ? 0 : (int)((context.Secondary + 1.0) / 2.0 * secondaryRange);

            return depth <= 1 + offset + surface + secondary;
        };
    }

    private int ResolveAnchor(JsonElement json)
    {
        var anchor = Obsidian.WorldData.Generators.Mojang.Carvers.CarverAnchor.Parse(json);
        return anchor.Resolve(this.generator.Settings.MinY, this.generator.Settings.Height);
    }

    internal static IBlock ReadBlock(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.String)
        {
            return BlocksRegistry.Get(WorldgenData.Qualify(json.GetString()!));
        }

        var id = WorldgenData.Qualify(json.GetProperty("id").GetString()!);
        var properties = new Dictionary<string, string>();

        if (json.TryGetProperty("properties", out var values))
        {
            foreach (var property in values.EnumerateObject())
            {
                properties.Add(property.Name, property.Value.GetString()!);
            }
        }

        return BlocksRegistry.GetFromSimpleState(new SimpleBlockState
        {
            Name = id,
            Properties = properties
        });
    }

    internal static int SurfaceHeight(IChunk chunk, int x, int z) =>
        chunk.Heightmaps[HeightmapType.WorldSurfaceWG].GetHeight(x, z);

    /// <summary>What a material column needs to know about a block.</summary>
    private enum BlockKind : byte
    {
        Air,
        Liquid,

        /// <summary>Any other block, which rules may replace.</summary>
        Solid
    }

    /// <summary>
    /// The kinds of a column's blocks from one below the chunk up to <paramref name="top"/>, indexed from one below.
    /// </summary>
    private static void ReadKinds(IChunk chunk, int x, int z, int top, BlockKind[] kinds)
    {
        var minY = chunk.MinY;
        kinds[0] = BlockKind.Air;

        for (var y = minY; y <= top; y++)
        {
            var section = chunk.Sections[(y - minY) >> 4] as ChunkSection;
            var kind = section is not null
                ? KindOf(BlocksRegistry.RegistryIdOf(section.GetStateId(x, y & 15, z)))
                : KindOf(chunk.GetBlock(x, y, z).RegistryId);

            kinds[y - minY + 1] = kind;
        }
    }

    // As IBlock.IsAir and IBlock.IsLiquid tell them apart.
    private static BlockKind KindOf(int registryId) => registryId switch
    {
        BlockIds.Air or BlockIds.CaveAir or BlockIds.VoidAir => BlockKind.Air,
        BlockIds.Water or BlockIds.Lava => BlockKind.Liquid,
        _ => BlockKind.Solid
    };
}
