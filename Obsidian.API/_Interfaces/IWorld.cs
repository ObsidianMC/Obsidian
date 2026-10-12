using Obsidian.API.Registry.Codecs.Dimensions;

namespace Obsidian.API;

public interface IWorld : ILevel
{
    public string PlayerDataPath { get; }
    public IWorldManager WorldManager { get; }

    /// <summary>
    /// Adds an initialized dimension to the world, by its <see cref="ILevel.DimensionName"/>.
    /// </summary>
    public void RegisterDimension(IDimension dimension);
}
