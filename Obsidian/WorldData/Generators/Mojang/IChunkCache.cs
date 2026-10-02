namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// A cache in a noise chunk's density functions, emptied when the noise chunk moves to another chunk (see
/// <see cref="NoiseChunk.MoveTo"/>): values inside and outside a chunk can differ, so cached values don't carry over.
/// </summary>
internal interface IChunkCache
{
    /// <summary>
    /// Forgets every cached value.
    /// </summary>
    public void Reset();
}
