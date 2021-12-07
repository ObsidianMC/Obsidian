namespace Obsidian.ChunkData;
public interface IBiomePalette : IPalette
{
    public int GetIdFromBiome(Biomes biome);

    public Biomes GetBiomeFromIndex(int index);
}

