using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API;

/// <summary>Helpers for working with <see cref="BlockFace"/> values.</summary>
public static class BlockFaceExtensions
{
    // By face, in the enum's order. Table lookups keep these small enough to inline in generation's hot loops.
    private static readonly Vector[] faceVectors = [Vector.Down, Vector.Up, Vector.North, Vector.South, Vector.West, Vector.East];

    /// <summary>
    /// Unit vector pointing out of the face (e.g. <see cref="BlockFace.Up"/> is <c>(0, 1, 0)</c>).
    /// </summary>
    public static Vector ToVector(this BlockFace face) =>
        (uint)face < (uint)faceVectors.Length ? faceVectors[(int)face] : ThrowInvalidFace<Vector>(face);

    // Opposite faces are pairs in the enum (down/up, north/south, west/east).
    /// <summary>
    /// Gets the face on the other side of the block, such as <see cref="BlockFace.Up"/> for
    /// <see cref="BlockFace.Down"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="face"/> is not a defined face.</exception>
    public static BlockFace Opposite(this BlockFace face) =>
        (uint)face <= (uint)BlockFace.East ? (BlockFace)((int)face ^ 1) : ThrowInvalidFace<BlockFace>(face);

    [DoesNotReturn]
    private static T ThrowInvalidFace<T>(BlockFace face) => throw new ArgumentOutOfRangeException(nameof(face), face, null);
}
