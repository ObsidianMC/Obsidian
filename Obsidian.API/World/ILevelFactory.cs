namespace Obsidian.API.World;

public interface ILevelFactory
{
    /// <summary>
    /// Creates a new world with the specified name and generator.
    /// </summary>
    /// <param name="name">The name of the world to create.</param>
    /// <param name="generatorId">The ID of the world generator to use.</param>
    /// <returns>The created world.</returns>
    public IWorld CreateWorld(string name, string generatorId);

    public IDimension CreateDimension(string name, string generatorId);
}
