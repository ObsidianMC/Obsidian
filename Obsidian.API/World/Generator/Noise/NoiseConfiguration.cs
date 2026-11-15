namespace Obsidian.API.World.Generator.Noise;
/// <summary>
/// Describes the vertical extent and sampling cell sizes of terrain noise.
/// </summary>
public sealed class NoiseConfiguration
{
    /// <summary>
    /// Gets or sets the generation height in blocks, read from <c>height</c>.
    /// </summary>
    public required int Height { get; set; }

    /// <summary>
    /// Gets or sets the minimum generation Y in blocks, read from <c>min_y</c>.
    /// </summary>
    public required int MinY { get; set; }

    /// <summary>
    /// Gets or sets the horizontal noise cell size in units of four blocks, read from <c>size_horizontal</c>.
    /// </summary>
    public required int SizeHorizontal { get; set; }

    /// <summary>
    /// Gets or sets the vertical noise cell size in units of four blocks, read from <c>size_vertical</c>.
    /// </summary>
    public required int SizeVertical { get; set; }
}

