using Obsidian.API.Noise;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Generators.Mojang;

namespace Obsidian.WorldData.Generators.Terrain;

internal sealed partial class MaterialRules
{
    private static readonly PerlinSimplexNoise temperatureNoise = new(
        new WorldgenRandom(new LegacyRandomSource(1234L)), [0]);
    private static readonly PerlinSimplexNoise frozenNoise = new(
        new WorldgenRandom(new LegacyRandomSource(3456L)), [-2, -1, 0]);
    private static readonly PerlinSimplexNoise biomeNoise = new(
        new WorldgenRandom(new LegacyRandomSource(2345L)), [0]);

    private float Temperature(string biome, int x, int y, int z)
    {
        var json = this.generator.Data.Read("biome", biome);
        var value = json.GetProperty("temperature").GetSingle();

        if (json.TryGetProperty("temperature_modifier", out var modifier) && modifier.GetString() == "frozen")
        {
            var frozen = frozenNoise.GetValue(x * 0.05, z * 0.05, false) * 7.0;
            var info = biomeNoise.GetValue(x * 0.2, z * 0.2, false);

            if (frozen + info < 0.3 && biomeNoise.GetValue(x * 0.09, z * 0.09, false) < 0.8)
            {
                value = 0.2f;
            }
        }

        var snowLine = this.generator.Settings.SeaLevel + 17;

        if (y <= snowLine)
        {
            return value;
        }

        // Biome temperature retains its fixed-seed simplex noises and float altitude adjustment in 26.3.
        var noise = (float)(temperatureNoise.GetValue(x / 8.0f, z / 8.0f, false) * 8.0);
        return value - (noise + y - snowLine) * 0.05f / 40.0f;
    }

    private float Noise(string name, double x, double z) =>
        this.generator.Compiler.Noise($"minecraft:{name}").Get(x, 0.0, z);

    private void ExtendBadlands(MaterialContext context, int startY)
    {
        var x = context.X;
        var z = context.Z;

        // Vanilla widens the surface noise before scaling it, but scales the pillar noise by 15.0f in float.
        var pillar = Math.Min(
            Math.Abs(this.Noise("badlands_surface", x, z) * 8.25),
            this.Noise("badlands_pillar", x * 0.2, z * 0.2) * 15.0f);

        if (pillar <= 0.0)
        {
            return;
        }

        var roof = Math.Abs(this.Noise("badlands_pillar_roof", x * 0.75, z * 0.75) * 1.5);
        var top = (int)Math.Floor(64.0 + Math.Min(pillar * pillar * 2.5, Math.Ceiling(roof * 50.0) + 24.0));

        if (startY > top)
        {
            return;
        }

        for (var y = top; y >= context.Terrain.Chunk.MinY; y--)
        {
            var block = context.GetBlock(y);

            if (block.RegistryId == this.generator.DefaultBlock.RegistryId)
            {
                break;
            }

            if (block.Is(BlockIds.Water))
            {
                return;
            }
        }

        for (var y = top; y >= context.Terrain.Chunk.MinY && context.GetBlock(y).IsAir; y--)
        {
            context.SetBlock(y, this.generator.DefaultBlock);
        }
    }

    private void ExtendIceberg(MaterialContext context, string biome, int startY)
    {
        var x = context.X;
        var z = context.Z;
        var sea = this.generator.Settings.SeaLevel;

        // As for badlands pillars, only the pillar noise is scaled in float.
        var iceberg = Math.Min(
            Math.Abs(this.Noise("iceberg_surface", x, z) * 8.25),
            this.Noise("iceberg_pillar", x * 1.28, z * 1.28) * 15.0f);

        if (iceberg <= 1.8)
        {
            return;
        }

        var roof = Math.Abs(this.Noise("iceberg_pillar_roof", x * 1.17, z * 1.17) * 1.5);
        var top = Math.Min(iceberg * iceberg * 1.2, Math.Ceiling(roof * 40.0) + 14.0);

        if (this.Temperature(biome, x, sea, z) > 0.1f)
        {
            top -= 2.0;
        }

        var bottom = 0.0;

        if (top > 2.0)
        {
            bottom = sea - top - 7.0;
            top += sea;
        }
        else
        {
            top = 0.0;
        }

        // These draws are positional, independent of column traversal and of material rule random sequences.
        var random = new PositionalRandom(this.generator.Compiler.Random, x, 0, z);
        var maxSnow = 2 + random.NextInt(4);
        var minSnowY = sea + 18 + random.NextInt(10);
        var snow = 0;

        for (var y = Math.Max(startY, (int)top + 1); y >= context.MinSurface; y--)
        {
            var block = context.GetBlock(y);
            var place = block.IsAir && y < (int)top && random.NextDouble() > 0.01;

            if (!place && block.Is(BlockIds.Water) && y > (int)bottom && y < sea && bottom != 0.0)
            {
                place = random.NextDouble() > 0.15;
            }

            if (!place)
            {
                continue;
            }

            if (snow <= maxSnow && y > minSnowY)
            {
                context.SetBlock(y, BlocksRegistry.SnowBlock);
                snow++;
            }
            else
            {
                context.SetBlock(y, BlocksRegistry.PackedIce);
            }
        }
    }

    internal static void UpdateHeight(IChunk chunk, HeightmapType type, int x, int y, int z, IBlock block)
    {
        var heightmap = chunk.Heightmaps[type];
        var height = heightmap.GetHeight(x, z);

        if (WorldgenHeightmaps.Matches(type, block))
        {
            if (y >= height)
            {
                heightmap.Set(x, z, y + 1);
            }

            return;
        }

        if (y != height - 1)
        {
            return;
        }

        for (var below = y - 1; below >= chunk.MinY; below--)
        {
            if (WorldgenHeightmaps.Matches(type, chunk.GetBlock(x, below, z)))
            {
                heightmap.Set(x, z, below + 1);
                return;
            }
        }

        heightmap.Set(x, z, chunk.MinY);
    }

    private static IBlock[] GenerateBands(IRandomSource random)
    {
        var bands = new IBlock[192];
        Array.Fill(bands, BlocksRegistry.Terracotta);

        for (var i = 0; i < bands.Length; i++)
        {
            i += random.NextInt(5) + 1;
            if (i < bands.Length)
            {
                bands[i] = BlocksRegistry.OrangeTerracotta;
            }
        }

        MakeBands(random, bands, 1, BlocksRegistry.YellowTerracotta);
        MakeBands(random, bands, 2, BlocksRegistry.BrownTerracotta);
        MakeBands(random, bands, 1, BlocksRegistry.RedTerracotta);

        var whiteBandCount = random.NextIntBetweenInclusive(9, 15);
        var placed = 0;

        for (var i = 0; placed < whiteBandCount && i < bands.Length; i += random.NextInt(16) + 4)
        {
            bands[i] = BlocksRegistry.WhiteTerracotta;

            if (i - 1 > 0 && random.NextBoolean())
            {
                bands[i - 1] = BlocksRegistry.LightGrayTerracotta;
            }

            if (i + 1 < bands.Length && random.NextBoolean())
            {
                bands[i + 1] = BlocksRegistry.LightGrayTerracotta;
            }

            placed++;
        }

        return bands;
    }

    private static void MakeBands(IRandomSource random, IBlock[] bands, int minWidth, IBlock block)
    {
        var count = random.NextIntBetweenInclusive(6, 15);

        for (var i = 0; i < count; i++)
        {
            var width = minWidth + random.NextInt(3);
            var start = random.NextInt(bands.Length);

            for (var offset = 0; start + offset < bands.Length && offset < width; offset++)
            {
                bands[start + offset] = block;
            }
        }
    }

}
