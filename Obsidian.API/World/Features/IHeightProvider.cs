using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features;

/// <summary>
/// Samples a Y level, like vanilla's HeightProvider.
/// </summary>
public interface IHeightProvider
{
    /// <summary>
    /// Gets the registered codec type identifier for this world generation component.
    /// </summary>
    public string Type { get; }

    /// <summary>
    /// Samples a world Y in blocks using the supplied random source and generation bounds.
    /// </summary>
    public int Sample(IRandomSource random, WorldGenerationContext context);
}
