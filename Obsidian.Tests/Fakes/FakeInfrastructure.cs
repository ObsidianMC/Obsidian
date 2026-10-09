using Microsoft.Extensions.Options;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Events;
using Obsidian.API.World;
using Obsidian.API.Plugins;
using Obsidian.API.Registry.Codecs.Dimensions;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;

namespace Obsidian.Tests.Fakes;

internal sealed class TestOptionsMonitor<T>(T currentValue) : IOptionsMonitor<T>
{
    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose() { }
    }

    public T CurrentValue { get; private set; } = currentValue;

    public T Get(string? name) => this.CurrentValue;

    public IDisposable OnChange(Action<T, string?> listener) => new NoopDisposable();

    public void Set(T value) => this.CurrentValue = value;
}

internal sealed class FakePacketBroadcaster : IPacketBroadcaster
{
    public void Broadcast(IClientboundPacket packet, params int[] excludedIds) { }
    public void BroadcastTo(IClientboundPacket packet, params int[] ids) { }
    public void BroadcastToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds) { }
    public void BroadcastToLevelInRange(ILevel level, VectorD location, IClientboundPacket packet, params int[] excludedIds) { }
    public void QueuePacketTo(IClientboundPacket packet, params int[] ids) { }
    public void QueuePacketTo(IClientboundPacket packet, int priority, params int[] ids) { }
    public void QueuePacketToLevel(ILevel toLevel, IClientboundPacket packet, params int[] excludedIds) { }
    public void QueuePacketToLevelInRange(ILevel level, VectorD location, IClientboundPacket packet, params int[] excludedIds) { }
    public void QueuePacket(IClientboundPacket packet, params int[] excludedIds) { }
    public void QueuePacketToLevel(ILevel toLevel, int priority, IClientboundPacket packet, params int[] excludedIds) { }
    public void QueuePacket(IClientboundPacket packet, int priority, params int[] excludedIds) { }
}

internal sealed class FakeEventDispatcher : IEventDispatcher
{
    public ValueTask<EventResult> ExecuteEventAsync<TEventArgs>(TEventArgs eventArgs) where TEventArgs : BaseMinecraftEventArgs =>
        ValueTask.FromResult(EventResult.Completed);

    public void RegisterEvents(IPluginContainer? pluginContainer = null) { }

    public void Dispose() { }
}

internal sealed class FakeWorldManager(IWorld defaultWorld) : IWorldManager
{
    public bool ReadyToJoin => true;
    public int GeneratingChunkCount => 0;
    public int LoadedChunkCount => 0;
    public int RegionCount => 0;
    public IWorld DefaultWorld { get; } = defaultWorld;

    public IReadOnlyCollection<IWorld> GetAvailableWorlds() => [this.DefaultWorld];

    public Task FlushLoadedWorldsAsync() => Task.CompletedTask;

    public Task TickWorldsAsync() => Task.CompletedTask;

    public bool TryGetWorld(string name, [NotNullWhen(true)] out IWorld? world)
    {
        world = name == this.DefaultWorld.Name ? this.DefaultWorld : null;
        return world is not null;
    }

    public bool TryGetWorld<TWorld>(string name, [NotNullWhen(true)] out TWorld? world) where TWorld : IWorld
    {
        world = this.DefaultWorld is TWorld typed && typed.Name == name ? typed : default;
        return world is not null;
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}

internal static class TestWorldFactory
{
    public static World Create(string name)
    {
        var configuration = new ServerConfiguration { SpawnChunkRadius = 0, TimeTickSpeedMultiplier = 1 };
        var world = new World(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<World>.Instance,
            new FakeWorldManager(defaultWorld: null!),
            new FakePacketBroadcaster(),
            new TestOptionsMonitor<ServerConfiguration>(configuration),
            new FakeEventDispatcher(),
            new EmptyWorldGenerator(),
            name,
            "test-seed");

        world.Initialize(new DimensionCodec
        {
            Name = "minecraft:overworld",
            Id = 0,
            Element = new DimensionElement
            {
                MonsterSpawnBlockLightLimit = 0,
                MonsterSpawnLightLevel = new MonsterSpawnLightLevel { IntValue = 0 },
                PiglinSafe = false,
                Natural = true,
                AmbientLight = 0,
                RespawnAnchorWorks = false,
                HasSkylight = true,
                BedWorks = true,
                HasRaids = true,
                MinY = -64,
                Height = 384,
                LogicalHeight = 384,
                CoordinateScale = 1,
                Ultrawarm = false,
                HasCeiling = false
            }
        });

        world.LevelData.SpawnPosition = new VectorD(0.5, 64, 0.5);
        return world;
    }
}
