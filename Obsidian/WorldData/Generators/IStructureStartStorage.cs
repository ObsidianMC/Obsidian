using Obsidian.Nbt;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// A generator whose structure starts keep state in their start chunk, which regions save with the chunk.
/// </summary>
internal interface IStructureStartStorage
{
    /// <summary>Pins starts needed by a chunk published in region storage.</summary>
    public void TrackStructureChunk(IChunk chunk);

    /// <summary>Releases starts after a chunk's final save and removal from region storage.</summary>
    public void UntrackStructureChunk(IChunk chunk);

    /// <summary>
    /// The structure starts to save in a chunk (vanilla's <c>structures.starts</c>), given the ones it was loaded with;
    /// <c>null</c> when there are none.
    /// </summary>
    public NbtCompound? SaveStructureStarts(int chunkX, int chunkZ, NbtCompound? loaded);
}
