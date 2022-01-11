using Microsoft.Extensions.Logging;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData;

public class WorldManager
{
    public int GeneratingChunkCount => worlds.SelectMany(x => x.Regions.Where(r => r.Value is not null)).Sum(r => r.Value.LoadedChunkCount);

    public int RegionCount => worlds.Sum(x => x.Regions.Count());

    public int LoadedChunkCount => worlds.Sum(x => x.Regions.Sum(x => x.Value.LoadedChunkCount));

    public World Primary => this.GetWorld(0);

    private readonly Server server;
    private readonly List<World> worlds;
    private readonly ILogger logger;

    public WorldManager(Server server, ILogger logger)
    {
        this.server = server;
        worlds = new List<World>();
        this.logger = logger;
    }

    public async Task LoadWorldsAsync()
    {
        foreach(var configWorld in this.server.Config.Worlds)
        {
            if (!server.WorldGenerators.TryGetValue(configWorld.Generator, out Type value))
                logger.LogWarning($"Unknown generator type {configWorld.Generator}");

            var world = new World(configWorld.Name, configWorld.Seed, this.server, value);
            if (!await world.LoadAsync())
            {
                logger.LogInformation($"Creating new world: {configWorld.Name}...");
                await world.Init();
                world.Save();
            }

            this.worlds.Add(world);
        }
    }

    public World GetWorld(int index)
    {
        return worlds[index];
    }

    public IReadOnlyCollection<World> GetAvailableWorlds()
    {
        return this.worlds.AsReadOnly();
    }

    public bool TryGetWorldByName(string name, [NotNullWhen(true)]out World? world)
    {
        world = this.worlds.FirstOrDefault(x => x.Name == name);

        return world != null;
    }

    public bool TryGetWorld(int index, out World? world)
    {
        if (index >= worlds.Count || index < 0)
        {
            world = null;
            return false;
        }

        world = worlds[index];
        return true;
    }

    public async Task TickWorldsAsync()
    {
        foreach(var world in worlds)
        {
            await world.DoWorldTickAsync();
        }
    }

    public async Task FlushLoadedWorldsAsync()
    {
        foreach(var world in worlds)
        {
            await world.FlushRegionsAsync();
        }
    }
}
