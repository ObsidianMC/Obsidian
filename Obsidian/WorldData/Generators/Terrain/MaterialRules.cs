using Obsidian.API.World.Generator.RandomSources;
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

    public MaterialRules(TerrainGenerator generator)
    {
        this.generator = generator;
        this.bands = GenerateBands(generator.Compiler.Random.FromHashOf("minecraft:clay_bands"));
        this.root = this.CompileRule(generator.Settings.MaterialRule);
    }

    public void Apply(TerrainContext terrain)
    {
        var context = new MaterialContext(this, terrain);
        var chunk = terrain.Chunk;

        // Each column sees the heightmaps updated by earlier columns, including badlands and iceberg extensions.
        // This is vanilla's x-major surface traversal, distinct from the density buffer's z-major order.
        for (var x = 0; x < 16; x++)
        {
            for (var z = 0; z < 16; z++)
            {
                var startY = SurfaceHeight(chunk, x, z);
                var biome = terrain.BiomeAt(terrain.Volume.BlockX(x), startY, terrain.Volume.BlockZ(z));
                context.UpdateColumn(x, z);

                if (biome == "minecraft:eroded_badlands")
                {
                    this.ExtendBadlands(context, startY);
                }

                this.ApplyColumn(context, SurfaceHeight(chunk, x, z));

                if (biome is "minecraft:frozen_ocean" or "minecraft:deep_frozen_ocean")
                {
                    this.ExtendIceberg(context, biome, startY);
                }
            }
        }
    }

    private void ApplyColumn(MaterialContext context, int startY)
    {
        var above = 0;
        var water = int.MinValue;
        var bottom = int.MaxValue;
        var minY = context.Terrain.Chunk.MinY;

        for (var y = startY; y >= minY; y--)
        {
            var block = context.GetBlock(y);

            if (block.IsAir)
            {
                above = 0;
                water = int.MinValue;
                continue;
            }

            if (block.IsLiquid)
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
                    var below = context.GetBlock(belowY);

                    if (below.IsAir || below.IsLiquid)
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

    /// <summary>A freshly exposed surface during carving uses depth one and a one-block density volume.</summary>
    public IBlock? TopMaterial(TerrainContext terrain, int x, int y, int z, bool underwater)
    {
        var context = new MaterialContext(this, terrain, new DensityVolume(1, 1, 1, x, y, z));
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
        var ore = ReadBlock(json.GetProperty("ore_block"));
        var raw = ReadBlock(json.GetProperty("raw_ore_block"));
        var filler = ReadBlock(json.GetProperty("filler_block"));
        var rawChance = json.GetProperty("raw_ore_chance").GetSingle();
        var factory = compiler.RandomFactory("minecraft:ore");

        return context =>
        {
            var chance = context.Density(density, volume: true);

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

            var oreChance = context.Density(richness, volume: true);

            if (random.NextFloat() < oreChance && context.Density(gap, volume: false) < 0.0f)
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
                    ? new HashSet<string> { WorldgenData.Qualify(names.GetString()!) }
                    : names.EnumerateArray().Select(name => WorldgenData.Qualify(name.GetString()!)).ToHashSet();
                return context => biomes.Contains(context.Biome);
            case "minecraft:not":
                var inverted = this.CompileCondition(json.GetProperty("invert"));
                return context => !inverted(context);
            case "minecraft:noise_threshold":
                var noise = this.generator.Compiler.Noise(WorldgenData.Qualify(json.GetProperty("noise").GetString()!));
                var min = json.GetProperty("min_threshold").GetDouble();
                var max = json.GetProperty("max_threshold").GetDouble();
                var is3d = json.TryGetProperty("is_3d", out var is3dJson) && is3dJson.GetBoolean();
                return context =>
                {
                    var value = context.Noise(noise, is3d);
                    return value >= min && value <= max;
                };
            case "minecraft:vertical_gradient":
                return this.CompileGradient(json);
            case "minecraft:stone_depth":
                return CompileStoneDepth(json);
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

    private static Func<MaterialContext, bool> CompileStoneDepth(JsonElement json)
    {
        var ceiling = json.GetProperty("surface_type").GetString() == "ceiling";
        var offset = json.GetProperty("offset").GetInt32();
        var addSurface = json.GetProperty("add_surface_depth").GetBoolean();
        var secondaryRange = json.GetProperty("secondary_depth_range").GetInt32();

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
}
