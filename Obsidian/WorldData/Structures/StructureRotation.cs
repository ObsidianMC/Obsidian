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
    /// Vanilla <c>BlockState.rotate</c> for the properties template blocks use: pillar <c>axis</c> swaps X and Z on quarter
    /// turns, horizontal <c>facing</c> turns with the structure, and so do the horizontal connections of fences, walls, panes,
    /// vines and the like. Other properties are kept.
    /// </summary>
    public static IBlock Rotate(this StructureRotation rotation, IBlock block)
    {
        if (rotation == StructureRotation.None)
            return block;

        var axis = block.GetProperty("axis");
        if (axis is not null && rotation != StructureRotation.Clockwise180)
            block = block.WithProperty("axis", axis switch { "x" => "z", "z" => "x", _ => axis });

        var facing = block.GetProperty("facing");
        if (facing is "north" or "east" or "south" or "west")
            block = block.WithProperty("facing", RotateFacing(facing, rotation));

        return RotateConnections(block, rotation);
    }

    /// <summary>
    /// Moves each horizontal connection value (<c>north</c>, <c>east</c>, ...) to its rotated side. Chorus plants and fire
    /// don't override vanilla's <c>rotate</c>, so they keep theirs.
    /// </summary>
    private static IBlock RotateConnections(IBlock block, StructureRotation rotation)
    {
        string[] sides = ["north", "east", "south", "west"];
        var values = Array.ConvertAll(sides, block.GetProperty);
        if (Array.IndexOf(values, null) >= 0 || block.BlockClass() is "ChorusPlantBlock" or "FireBlock")
            return block;

        for (var i = 0; i < sides.Length; i++)
            block = block.WithProperty(RotateFacing(sides[i], rotation), values[i]!);

        return block;
    }

    private static string RotateFacing(string facing, StructureRotation rotation)
    {
        string[] clockwise = ["north", "east", "south", "west"];
        var turns = rotation switch
        {
            StructureRotation.Clockwise90 => 1,
            StructureRotation.Clockwise180 => 2,
            _ => 3
        };

        return clockwise[(Array.IndexOf(clockwise, facing) + turns) % 4];
    }
}
