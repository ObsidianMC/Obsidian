using Obsidian.WorldData.Generators.Density;
using Obsidian.WorldData.Generators.Mojang;
using Obsidian.WorldData.Structures;

namespace Obsidian.WorldData.Generators.Terrain;

/// <summary>
/// The terrain 26.3 structures plan against before any chunk exists, like vanilla 26.3's
/// <c>NoiseBasedChunkGenerator.iterateNoiseColumn</c>: one column's final density, without structures, through its
/// own aquifer, from the top down.
/// </summary>
internal sealed class TerrainColumns(TerrainGenerator generator) : IStructureTerrain
{
    public int SeaLevel => generator.Settings.SeaLevel;

    public int GetBaseHeight(int x, int z, HeightmapType heightmap) =>
        this.Iterate(x, z, block => WorldgenHeightmaps.Matches(heightmap, block), null) ?? generator.Settings.MinY;

    public NoiseColumn GetBaseColumn(int x, int z)
    {
        var blocks = new IBlock[generator.Settings.Height];
        this.Iterate(x, z, null, blocks);

        return new NoiseColumn(generator.Settings.MinY, blocks);
    }

    // Stops at the first block matching stopAt and gives the Y above it; fills blocks from the noise's min Y.
    private int? Iterate(int x, int z, Predicate<IBlock>? stopAt, IBlock[]? blocks)
    {
        var settings = generator.Settings;
        var volume = new DensityVolume(1, settings.Height, 1, x, settings.MinY, z);
        var aquifer = new TerrainAquifer(generator, volume);
        using var density = DensityBuffer.Sample(generator.Router.FinalDensity, volume);

        for (var y = volume.SizeY - 1; y >= 0; y--)
        {
            var blockY = volume.BlockY(y);
            var value = density.Span[volume.Index(0, y, 0)];
            var block = aquifer.ComputeSubstance(x, blockY, z, value) ?? generator.DefaultBlock;

            if (blocks is not null)
            {
                blocks[y] = block;
            }

            if (stopAt is not null && stopAt(block))
            {
                return blockY + 1;
            }
        }

        return null;
    }
}
