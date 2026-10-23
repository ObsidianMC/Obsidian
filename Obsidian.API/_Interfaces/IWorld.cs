using Obsidian.API.Registry.Codecs.Dimensions;

namespace Obsidian.API;

public interface IWorld : ILevel
{
    /// <summary>Finds the world's root level or a loaded dimension by its identifier, for portal routing.</summary>
    public ILevel? GetLevel(string identifier) => this.DimensionName == identifier ? this : null;

    public string PlayerDataPath { get; }
    public IWorldManager WorldManager { get; }

    /// <summary>
    /// Adds an initialized dimension to the world, by its <see cref="ILevel.DimensionName"/>.
    /// </summary>
    public void RegisterDimension(IDimension dimension);
}
