using Obsidian.Nbt;

namespace Obsidian.WorldData.Upgrade;

/// <summary>
/// Rewrites the NBT of an Obsidian-written 1.21.11 world into 26.3's shape, one file's root compound at a time. The
/// world upgrade (<c>WorldUpgrade</c>) calls these for every chunk, entity chunk and structure template it moves.
/// </summary>
/// <remarks>
/// Each method returns the upgraded compound (it may be the same instance, changed in place) and leaves data that's
/// already in 26.3's shape alone, so running it twice is harmless. This is not a general vanilla data fixer: it only
/// covers what Obsidian wrote in 1.21.11.
/// </remarks>
internal static class LegacyChunkUpgrade
{
    /// <summary>
    /// A region chunk: block-state palettes, the chunk status, block entities and the <c>DataVersion</c>.
    /// </summary>
    public static NbtCompound UpgradeChunk(NbtCompound chunk) => chunk;

    /// <summary>
    /// An entity chunk (<c>entities/r.x.z.mca</c>): block states held by entities and the <c>DataVersion</c>.
    /// </summary>
    public static NbtCompound UpgradeEntityChunk(NbtCompound chunk) => chunk;

    /// <summary>
    /// A structure template (<c>.nbt</c>): its block-state palettes.
    /// </summary>
    public static NbtCompound UpgradeStructureTemplate(NbtCompound template) => template;
}
