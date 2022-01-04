using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
            var world = new World(configWorld.Name, this.server);
            if (!await world.LoadAsync())
            {
                if (!server.WorldGenerators.TryGetValue(configWorld.Generator, out WorldGenerator value))
                    logger.LogWarning($"Unknown generator type {configWorld.Generator}");

                var gen = value ?? server.WorldGenerators.First().Value;
                logger.LogInformation($"Creating new {gen.Id} ({gen}) world...");
                await world.Init(gen);
                world.Save();
            }

            this.worlds.Add(world);
        }
    }

    public World GetWorld(int index)
    {
        return worlds[index];
    }

    public bool TryGetWorldByName(string name, out World? world)
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
