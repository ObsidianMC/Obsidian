using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.API;

/// <summary>
/// Samples a float, like vanilla's FloatProvider.
/// </summary>
public interface IFloatProvider : IRegistryResource
{
    public new string Type { get; }

    public float MinValue { get; }

    public float MaxValue { get; }

    public float Sample(IRandomSource random);
}
