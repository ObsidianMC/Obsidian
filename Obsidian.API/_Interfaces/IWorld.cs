using Obsidian.API.Registry.Codecs.Dimensions;

namespace Obsidian.API;

/// <summary>
/// A world: its overworld level, which this is, and its other dimensions.
/// </summary>
public interface IWorld : ILevel
{
    /// <summary>Finds the world's root level or a loaded dimension by its identifier, for portal routing.</summary>
    public ILevel? GetLevel(string identifier) => this.DimensionName == identifier ? this : null;

    /// <summary>
    /// Gets the folder where the world's player data is saved.
    /// </summary>
    public string PlayerDataPath { get; }
    /// <summary>
    /// Gets the world manager the world belongs to.
    /// </summary>
    public IWorldManager WorldManager { get; }

    /// <summary>
    /// Adds an initialized dimension to the world, by its <see cref="ILevel.DimensionName"/>.
    /// </summary>
    public void RegisterDimension(IDimension dimension);
}
