namespace Obsidian.WorldData.Structures.Pools;

/// <summary>
/// How far a jigsaw structure's pieces may reach from its start's center, like vanilla's
/// <c>JigsawStructure.MaxDistance</c>: a single number is both distances.
/// </summary>
/// <param name="Horizontal">Along X and Z, from 1 to 128.</param>
/// <param name="Vertical">Along Y, from 1 to 4064; vanilla's object form defaults it to 4064.</param>
public readonly record struct JigsawMaxDistance(int Horizontal, int Vertical)
{
    /// <summary>
    /// The largest horizontal distance vanilla's codec accepts.
    /// </summary>
    public const int MaxHorizontal = 128;

    /// <summary>
    /// The largest vertical distance, and that of vanilla's object form without <c>vertical</c>:
    /// <c>DimensionType.Y_SIZE</c>.
    /// </summary>
    public const int MaxVertical = 4064;

    public static implicit operator JigsawMaxDistance(int distance) => new(distance, distance);
}

/// <summary>
/// Blocks a jigsaw structure's pieces keep from the bottom and top of the world, like vanilla's
/// <c>DimensionPadding</c>: a single number is both. A structure without padding doesn't check its start piece
/// against the world's height at all, while one with explicit padding, even of zero, does.
/// </summary>
public readonly record struct DimensionPadding(int Bottom, int Top)
{
    public static implicit operator DimensionPadding(int padding) => new(padding, padding);
}
