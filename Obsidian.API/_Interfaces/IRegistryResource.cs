namespace Obsidian.API;
/// <summary>
/// A worldgen object that data files name by its type, like a density function or a block state provider.
/// </summary>
public interface IRegistryResource
{
    /// <summary>
    /// Gets the identifier of the object's type, like <c>minecraft:constant</c>.
    /// </summary>
    public string Type { get; }
}
