using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Maps;

/// <summary>
/// Draws maps like vanilla's <c>MapItem</c>: the terrain around the player holding one, and the biome preview of explorer maps.
/// </summary>
internal static class MapRenderer
{
    private static readonly BiomeSet waterOnMapOutlines = new("#minecraft:water_on_map_outlines");

    /// <summary>
    /// Vanilla <c>MapItem.update</c>: renders one band of columns (every 16th, in turn) of the terrain within reach of the
    /// player, shading each pixel by its height against its northern neighbor.
    /// </summary>
    /// <remarks>
    /// Only loaded chunks are drawn; vanilla loads the chunks under the map.
    /// </remarks>
    public static async ValueTask UpdateAsync(AbstractLevel level, Player player, MapData data)
    {
        if (level.DimensionName != data.Dimension)
            return;

        var scale = 1 << data.Scale;
        var centerX = data.CenterX;
        var centerZ = data.CenterZ;
        var playerX = (int)Math.Floor(player.Position.X - centerX) / scale + 64;
        var playerZ = (int)Math.Floor(player.Position.Z - centerZ) / scale + 64;
        var hasCeiling = CodecRegistry.TryGetDimension(level.DimensionName, out var codec) && codec!.Element.HasCeiling;
        var radius = MapData.Size / scale;
        if (hasCeiling)
            radius /= 2;

        var holder = data.GetHoldingPlayer(player);
        holder.Step++;
        var redrawNext = false;

        for (var x = playerX - radius + 1; x < playerX + radius; x++)
        {
            if ((x & 15) != (holder.Step & 15) && !redrawNext)
                continue;

            redrawNext = false;
            var previousHeight = 0.0;

            for (var z = playerZ - radius - 1; z < playerZ + radius; z++)
            {
                if (x < 0 || z < -1 || x >= MapData.Size || z >= MapData.Size)
                    continue;

                var distance = (x - playerX) * (x - playerX) + (z - playerZ) * (z - playerZ);
                var outerRing = distance > (radius - 2) * (radius - 2);
                var blockX = (centerX / scale + x - 64) * scale;
                var blockZ = (centerZ / scale + z - 64) * scale;
                var chunk = await level.GetChunkAsync(blockX >> 4, blockZ >> 4, false);
                if (chunk is null)
                    continue;

                var (color, height, waterDepth) = hasCeiling ? SampleCeilingNoise(blockX, blockZ) : SampleColumns(chunk, blockX, blockZ, scale);
                var brightness = color == MapColors.Water
                    ? WaterBrightness(waterDepth * 0.1 + ((x + z) & 1) * 0.2)
                    : LandBrightness((height - previousHeight) * 4.0 / (scale + 4) + (((x + z) & 1) - 0.5) * 0.4);

                previousHeight = height;
                if (z >= 0 && distance < radius * radius && (!outerRing || ((x + z) & 1) != 0))
                    redrawNext |= data.UpdateColor(x, z, MapColors.Pack(color, brightness));
            }
        }
    }

    /// <summary>
    /// Vanilla <c>MapItem.renderBiomePreviewMap</c>: outlines the water biomes of an explorer map in orange and brown.
    /// </summary>
    public static void RenderBiomePreview(MapData data, Func<int, int, BiomeCodec> biomeAt)
    {
        var scale = 1 << data.Scale;
        var originX = data.CenterX / scale - 64;
        var originZ = data.CenterZ / scale - 64;
        var water = new bool[MapData.Size * MapData.Size];

        for (var z = 0; z < MapData.Size; z++)
        {
            for (var x = 0; x < MapData.Size; x++)
                water[z * MapData.Size + x] = waterOnMapOutlines.Contains(biomeAt((originX + x) * scale, (originZ + z) * scale));
        }

        for (var x = 1; x < MapData.Size - 1; x++)
        {
            for (var z = 1; z < MapData.Size - 1; z++)
            {
                var waterNeighbors = 0;
                for (var dx = -1; dx < 2; dx++)
                {
                    for (var dz = -1; dz < 2; dz++)
                    {
                        if ((dx != 0 || dz != 0) && water[(z + dz) * MapData.Size + x + dx])
                            waterNeighbors++;
                    }
                }

                var brightness = MapBrightness.Lowest;
                var color = MapColors.None;
                if (water[z * MapData.Size + x])
                {
                    color = MapColors.Orange;
                    if (waterNeighbors > 7 && z % 2 == 0)
                    {
                        // Waves across open water.
                        switch ((x + (int)(Mth.Sin(z + 0.0f) * 7.0f)) / 8 % 5)
                        {
                            case 0 or 4:
                                brightness = MapBrightness.Low;
                                break;
                            case 1 or 3:
                                brightness = MapBrightness.Normal;
                                break;
                            case 2:
                                brightness = MapBrightness.High;
                                break;
                        }
                    }
                    else if (waterNeighbors > 7)
                    {
                        color = MapColors.None;
                    }
                    else if (waterNeighbors > 5)
                    {
                        brightness = MapBrightness.Normal;
                    }
                    else if (waterNeighbors > 1)
                    {
                        brightness = MapBrightness.Low;
                    }
                }
                else if (waterNeighbors > 0)
                {
                    color = MapColors.Brown;
                    brightness = waterNeighbors > 3 ? MapBrightness.Normal : MapBrightness.Lowest;
                }

                if (color != MapColors.None)
                    data.SetColor(x, z, MapColors.Pack(color, brightness));
            }
        }
    }

    /// <summary>
    /// The most common map color of the pixel's columns, their mean surface height (plus one), and the depth of their water.
    /// </summary>
    private static (byte Color, double Height, int WaterDepth) SampleColumns(IChunk chunk, int blockX, int blockZ, int scale)
    {
        var counts = new List<(byte Color, int Count)>();
        var height = 0.0;
        var waterDepth = 0;
        var minY = chunk.MinY;
        var heightmap = chunk.Heightmaps[HeightmapType.WorldSurface];

        for (var dx = 0; dx < scale; dx++)
        {
            for (var dz = 0; dz < scale; dz++)
            {
                var x = blockX + dx;
                var z = blockZ + dz;
                var y = heightmap.GetHeight(x & 15, z & 15);
                IBlock block;
                if (y <= minY)
                {
                    block = BlocksRegistry.Get(Material.Bedrock);
                }
                else
                {
                    do
                        block = chunk.GetBlock(x, --y, z);
                    while (MapColors.Get(block) == MapColors.None && y > minY);

                    if (y > minY && block.HasFluid())
                    {
                        var fluidY = y - 1;
                        IBlock below;
                        do
                        {
                            below = chunk.GetBlock(x, fluidY--, z);
                            waterDepth++;
                        }
                        while (fluidY > minY && below.HasFluid());

                        // Vanilla getCorrectStateForFluidBlock: waterlogged blocks without a sturdy top show their fluid.
                        if (!block.IsFaceSturdy(BlockFace.Up))
                            block = FluidBlock(block);
                    }
                }

                height += (double)y / (scale * scale);
                AddCount(counts, MapColors.Get(block), 1);
            }
        }

        return (MostCommon(counts), height, waterDepth / (scale * scale));
    }

    /// <summary>
    /// Vanilla's ceiling dimension pattern: a noisy mix of dirt and stone instead of the terrain.
    /// </summary>
    private static (byte Color, double Height, int WaterDepth) SampleCeilingNoise(int blockX, int blockZ)
    {
        var noise = blockX + blockZ * 231871;
        noise = noise * noise * 31287121 + noise * 11;
        var block = (noise >> 20 & 1) == 0 ? Material.Dirt : Material.Stone;
        return (MapColors.Get(BlocksRegistry.Get(block)), 100.0, 0);
    }

    private static IBlock FluidBlock(IBlock block) => block.GetFluid() switch
    {
        FluidKind.Lava or FluidKind.FlowingLava => BlocksRegistry.Get(Material.Lava),
        _ => BlocksRegistry.Get(Material.Water)
    };

    // Vanilla counts colors in a LinkedHashMultiset: the most common wins, the first seen on ties.
    private static void AddCount(List<(byte Color, int Count)> counts, byte color, int amount)
    {
        var index = counts.FindIndex(entry => entry.Color == color);
        if (index < 0)
            counts.Add((color, amount));
        else
            counts[index] = (color, counts[index].Count + amount);
    }

    private static byte MostCommon(List<(byte Color, int Count)> counts)
    {
        var best = (Color: MapColors.None, Count: 0);
        foreach (var entry in counts)
        {
            if (entry.Count > best.Count)
                best = entry;
        }

        return best.Color;
    }

    private static MapBrightness WaterBrightness(double shade) =>
        shade < 0.5 ? MapBrightness.High : shade > 0.9 ? MapBrightness.Low : MapBrightness.Normal;

    private static MapBrightness LandBrightness(double shade) =>
        shade > 0.6 ? MapBrightness.High : shade < -0.6 ? MapBrightness.Low : MapBrightness.Normal;
}
