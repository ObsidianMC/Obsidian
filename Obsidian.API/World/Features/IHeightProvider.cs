using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API.World.Features;

/// <summary>
/// Samples a Y level, like vanilla's HeightProvider.
/// </summary>
public interface IHeightProvider
{
    public string Type { get; }

    public int Sample(IRandomSource random, WorldGenerationContext context);
}
