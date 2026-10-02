using System.IO;
using System.Reflection;

namespace Obsidian.WorldData.Features.Tree;

/// <summary>
/// Which parts of a wall each block state covers with the bottom face of its collision shape, for vanilla's
/// <c>WallBlock</c> shape updates (tall sides and the raised post under a block).
/// </summary>
/// <remarks>
/// Loaded from <c>Assets/wall_shape_covers.bin</c>, one byte per block state id, dumped from vanilla 1.21.11: the bottom face
/// of <c>getCollisionShape</c> tested against <c>WallBlock.TEST_SHAPE_POST</c> and <c>TEST_SHAPES_WALL</c>.
/// </remarks>
internal static class WallShapeCovers
{
    public const int Post = 1;
    public const int North = 2;
    public const int East = 4;
    public const int South = 8;
    public const int West = 16;

    private static readonly Lazy<byte[]> covers = new(Load);

    /// <summary>The covered parts of a wall under <paramref name="block"/>, as <see cref="Post"/>, <see cref="North"/>... flags.</summary>
    public static int Get(IBlock block)
    {
        var id = block.StateId();
        var table = covers.Value;
        return id >= 0 && id < table.Length ? table[id] : 0;
    }

    private static byte[] Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Assets.wall_shape_covers.bin")
            ?? throw new InvalidOperationException("Missing wall shape covers asset.");
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
