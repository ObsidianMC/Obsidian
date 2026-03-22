using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData;

public sealed class LevelFactory(ILogger<LevelFactory> logger, IServiceScopeFactory serviceScopeFactory) : ILevelFactory
{
    private static readonly Type[] worldArgTypes = [typeof(ILevelGenerator), typeof(string), typeof(string)];
    private static readonly Type[] dimensionArgTypes = [typeof(ILevelGenerator), typeof(string), typeof(IWorld)];

    private ObjectFactory<World> worldFactory;
    private ObjectFactory<Dimension> dimensionFactory;

    private readonly ILogger<LevelFactory> logger = logger;
    private readonly IServiceScopeFactory serviceScopeFactory = serviceScopeFactory;

    public Dictionary<string, Type> LevelGenerators { get; } = [];

    public IDimension CreateDimension(IWorld parentWorld, string name, string generatorId)
    {
        Type? generatorType;
        if (!this.LevelGenerators.TryGetValue(generatorId, out generatorType))
            this.LevelGenerators.TryGetValue("overworld", out generatorType);

        if(generatorType == null)
            throw new InvalidOperationException($"Failed to find generator with id: {generatorId}");

        using var serviceProviderScope = this.serviceScopeFactory.CreateScope();

        var generator = (ILevelGenerator)ActivatorUtilities.CreateInstance(serviceProviderScope.ServiceProvider, generatorType);

        return this.dimensionFactory.Invoke(serviceProviderScope.ServiceProvider, [generator, name, parentWorld]);
    }

    public IWorld CreateWorld(string name, string seed, string generatorId)
    {
        Type? generatorType;
        if (!this.LevelGenerators.TryGetValue(generatorId, out generatorType))
            this.LevelGenerators.TryGetValue("overworld", out generatorType);

        if(generatorType == null)
            throw new InvalidOperationException($"Failed to find generator with id: {generatorId}");

        using var serviceProviderScope = this.serviceScopeFactory.CreateScope();

        var generator = (ILevelGenerator)ActivatorUtilities.CreateInstance(serviceProviderScope.ServiceProvider, generatorType);

        return this.worldFactory.Invoke(serviceProviderScope.ServiceProvider, [generator, name, seed]);
    }

    public void Initialize()
    {
        this.worldFactory = ActivatorUtilities.CreateFactory<World>(worldArgTypes);
        this.dimensionFactory = ActivatorUtilities.CreateFactory<Dimension>(dimensionArgTypes);

        this.RegisterDefaults();
    }

    /// <summary>
    /// Registers the "obsidian-vanilla" entities and objects.
    /// </summary>
    /// Might be used for more stuff later so I'll leave this here - tides
    private void RegisterDefaults()
    {
        this.RegisterGenerator<SuperflatGenerator>();
        this.RegisterGenerator<OverworldGenerator>();
        this.RegisterGenerator<IslandGenerator>();
        this.RegisterGenerator<EmptyWorldGenerator>();
        this.RegisterGenerator<MojangGenerator>();
    }

    /// <summary>
    /// Registers new world generator(s) to the server.
    /// </summary>
    /// <param name="entries">A compatible list of entries.</param>
    public void RegisterGenerator<T>() where T : ILevelGenerator, new()
    {
        var gen = new T();
        if (string.IsNullOrWhiteSpace(gen.Id))
            throw new InvalidOperationException($"Failed to get id for generator: {gen.Id}");

        if (this.LevelGenerators.TryAdd(gen.Id, typeof(T)))
            this.logger.LogDebug("Registered {generatorId}...", gen.Id);
    }
}
