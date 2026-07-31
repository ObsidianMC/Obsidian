namespace Obsidian.API.World;

public interface ILevelFactory
{
    /// <summary>
    /// Creates a new world with the specified name and generator.
    /// </summary>
    /// <param name="name">The name of the world to create.</param>
    /// <param name="seed">The seed to use for world generation.</param>
    /// <param name="generatorId">The namspaced ID of the world generator to use.</param>
    /// <returns>The created world.</returns>
    public IWorld CreateWorld(string name, string seed, string generatorId);

    /// <summary>
    /// Creates a new dimension within the specified parent world with the given name and generator.
    /// </summary>
    /// <param name="parentWorld">The parent world in which to create the dimension.</param>
    /// <param name="name">The name of the dimension to create.</param>
    /// <param name="generatorId">The ID of the dimension generator to use.</param>
    /// <returns>The created dimension.</returns>
    public IDimension CreateDimension(IWorld parentWorld, string name, string generatorId);

    /// <summary>
    /// Initializes the level factory, preparing it for use.
    /// This may involve setting up internal data structures, registering default generators, or performing any necessary setup tasks to ensure that the factory is ready to create worlds and dimensions.
    /// </summary>
    public void Initialize();

    /// <summary>
    /// Registers a new level generator of the specified type. 
    /// </summary>
    /// <typeparam name="T">The type of the level generator to register.</typeparam>
    public void RegisterGenerator<T>() where T : ILevelGenerator, new();
}
