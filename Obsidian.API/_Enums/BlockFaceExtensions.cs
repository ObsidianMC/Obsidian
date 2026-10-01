namespace Obsidian.API;

public static class BlockFaceExtensions
{
    /// <summary>
    /// Unit vector pointing out of the face (e.g. <see cref="BlockFace.Up"/> is <c>(0, 1, 0)</c>).
    /// </summary>
    public static Vector ToVector(this BlockFace face) => face switch
    {
        BlockFace.Down => Vector.Down,
        BlockFace.Up => Vector.Up,
        BlockFace.North => Vector.North,
        BlockFace.South => Vector.South,
        BlockFace.West => Vector.West,
        BlockFace.East => Vector.East,
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };

    public static BlockFace Opposite(this BlockFace face) => face switch
    {
        BlockFace.Down => BlockFace.Up,
        BlockFace.Up => BlockFace.Down,
        BlockFace.North => BlockFace.South,
        BlockFace.South => BlockFace.North,
        BlockFace.West => BlockFace.East,
        BlockFace.East => BlockFace.West,
        _ => throw new ArgumentOutOfRangeException(nameof(face))
    };
}
