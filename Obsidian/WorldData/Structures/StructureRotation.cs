using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Rotation of a structure template around the Y axis, like vanilla's <c>Rotation</c> (same declaration order).
/// </summary>
public enum StructureRotation
{
    None,
    Clockwise90,
    Clockwise180,
    CounterClockwise90
}

public static class StructureRotationExtensions
{
    private static readonly StructureRotation[] values = Enum.GetValues<StructureRotation>();

    /// <summary>Vanilla <c>Rotation.getRandom</c>: one <c>nextInt(4)</c>.</summary>
    public static StructureRotation Random(IRandomSource random) => values[random.NextInt(values.Length)];

    /// <summary>
    /// Vanilla <c>BlockState.rotate</c>.
    /// </summary>
    public static IBlock Rotate(this StructureRotation rotation, IBlock block) => block.Rotate(rotation);

    /// <summary>
    /// Vanilla <c>Rotation.getRotated</c>: this rotation followed by <paramref name="other"/>.
    /// </summary>
    public static StructureRotation GetRotated(this StructureRotation rotation, StructureRotation other) =>
        (StructureRotation)(((int)rotation + (int)other) & 3);

    /// <summary>
    /// Vanilla <c>Rotation.rotate(Direction)</c> for horizontal directions; vertical ones are kept.
    /// </summary>
    public static BlockFace Rotate(this StructureRotation rotation, BlockFace face)
    {
        if (face is BlockFace.Up or BlockFace.Down)
            return face;

        BlockFace[] clockwise = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];
        return clockwise[(Array.IndexOf(clockwise, face) + (int)rotation) & 3];
    }
}

/// <summary>
/// Mirroring of a structure template, like vanilla's <c>Mirror</c> (same declaration order). <see cref="LeftRight"/>
/// flips the Z axis and <see cref="FrontBack"/> the X axis.
/// </summary>
public enum StructureMirror
{
    None,
    LeftRight,
    FrontBack
}

public static class StructureMirrorExtensions
{
    /// <summary>
    /// Vanilla <c>BlockState.mirror</c>.
    /// </summary>
    public static IBlock Mirror(this StructureMirror mirror, IBlock block) => block.Mirror(mirror);

    /// <summary>
    /// Vanilla <c>Mirror.mirror(Direction)</c>: flips the direction along the mirrored axis.
    /// </summary>
    public static BlockFace Mirror(this StructureMirror mirror, BlockFace face) => (mirror, face) switch
    {
        (StructureMirror.LeftRight, BlockFace.North) => BlockFace.South,
        (StructureMirror.LeftRight, BlockFace.South) => BlockFace.North,
        (StructureMirror.FrontBack, BlockFace.East) => BlockFace.West,
        (StructureMirror.FrontBack, BlockFace.West) => BlockFace.East,
        _ => face
    };
}
