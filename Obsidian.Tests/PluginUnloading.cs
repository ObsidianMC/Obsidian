using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.API.Registries;
using Obsidian.Commands.Framework;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Plugins;
using Obsidian.Plugins.ServiceProviders;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// What unloading a plugin removes besides what it registered: the entities it owns, and what it added through the
/// server its handlers are given.
/// </summary>
// Loading plugins merges their tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class PluginUnloading : IDisposable
{
    private static readonly VectorD position = new(8, 70, 8);

    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-plugins-{Guid.NewGuid():N}");

    public PluginUnloading() => Directory.CreateDirectory(this.folder);

    public void Dispose()
    {
        string[] keys = [ProbePlugin.BatKey, ProbePlugin.BotKey, ProbePlugin.ServerKey, ProbePlugin.BossBarKey];

        foreach (var key in keys)
            AppContext.SetData(key, null);

        Directory.Delete(this.folder, recursive: true);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task Unload_RemovesTheEntitiesThePluginOwns()
    {
        await using var context = TestServerFactory.CreateServer($"probe-entities-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        var human = await JoinAsync(context);
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");

        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var container = (await manager.LoadPluginAsync(path))!;

        // The plugin's handler spawns a bat and adds a server player; the test, which isn't the plugin's code, spawns
        // one of the plugin's golems and a squid.
        var chat = new IncomingChatMessageEventArgs(human, context.Server, ProbePlugin.SpawnMessage, "");
        Assert.Equal(EventResult.Cancelled, await context.EventDispatcher.ExecuteEventAsync(chat));

        var bat = (int)AppContext.GetData(ProbePlugin.BatKey)!;
        var bot = (Guid)AppContext.GetData(ProbePlugin.BotKey)!;
        Assert.True(context.Server.IsPlayerOnline(bot));

        // The plugin's code spawns another outside any call into it, as its own task would.
        var spawnBat = container.Plugin!.GetType().GetMethod(nameof(ProbePlugin.SpawnBat))!;
        var strayBat = (IEntity)spawnBat.Invoke(container.Plugin, [context.World, position])!;

        Assert.True(EntityDefinitionsRegistry.TryGet("probe:golem", out var golemType));
        var golem = context.World.SpawnEntity(position, golemType);
        var squid = context.World.SpawnEntity(position, EntityType.Squid);

        var client = (TestClient)human.Client;
        client.ClearPackets();

        await manager.UnloadPluginAsync(container);

        // The bats, the golem and the server player are gone, from the level and the players' screens; the squid
        // stays.
        var left = context.World.Regions.Values.SelectMany(region => region.Entities.Keys).ToList();
        var removed = client.SentPackets
            .OfType<RemoveEntitiesPacket>()
            .SelectMany(packet => packet.Entities)
            .ToList();

        Assert.DoesNotContain(bat, left);
        Assert.DoesNotContain(strayBat.EntityId, left);
        Assert.DoesNotContain(golem.EntityId, left);
        Assert.Contains(squid.EntityId, left);
        Assert.Contains(bat, removed);
        Assert.Contains(strayBat.EntityId, removed);
        Assert.Contains(golem.EntityId, removed);
        Assert.False(context.Server.IsPlayerOnline(bot));

        // The golem is kept in its chunk as it was saved, like an entity of a type the server doesn't know.
        Assert.Contains(chunk.UnspawnableEntities, IsGolem);

        // Code still running for the unloaded plugin can't spawn for it.
        using (container.EnterUnloading())
            Assert.Throws<InvalidOperationException>(() => context.World.SpawnEntity(position, EntityType.Squid));

        // Loaded again, the plugin gets its golem back on the level's next tick.
        await manager.LoadPluginAsync(path);
        await context.World.DoWorldTickAsync();

        Assert.DoesNotContain(chunk.UnspawnableEntities, IsGolem);
        Assert.Contains(
            context.World.Regions.Values.SelectMany(region => region.Entities.Values),
            entity => entity.Definition == golemType);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PluginHandlers_GetThePluginsOwnServer()
    {
        await using var context = TestServerFactory.CreateServer($"probe-server-{Guid.NewGuid():N}");
        var human = await JoinAsync(context);
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");

        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var container = (await manager.LoadPluginAsync(path))!;

        // The handler cancels the message, which the server's handler after it sees in the shared arguments.
        var chat = new IncomingChatMessageEventArgs(human, context.Server, ProbePlugin.ServerMessage, "");
        Assert.Equal(EventResult.Cancelled, await context.EventDispatcher.ExecuteEventAsync(chat));

        // In the arguments, the handler found the plugin's own server; the test, outside the plugin, the server.
        var own = Assert.IsType<PluginServer>(container.HostServices.Server);
        Assert.Same(own, AppContext.GetData(ProbePlugin.ServerKey));
        Assert.Same(context.Server, chat.Server);

        // So the boss bar it created through it is the plugin's, which unloading retires.
        var bar = (IBossBar)AppContext.GetData(ProbePlugin.BossBarKey)!;
        bar.AddPlayer(human.EntityId);

        await manager.UnloadPluginAsync(container);

        Assert.Throws<InvalidOperationException>(() => bar.AddPlayer(human.EntityId));
    }

    // A connected player at the test's position, to see what the plugin's entities do.
    private static async Task<Obsidian.Entities.Player> JoinAsync(RealServerContext context)
    {
        var human = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Human", position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, human);

        return human;
    }

    // An empty plains chunk at the test's position, complete, so that its entities are saved and spawning ticks.
    private static Chunk AddEmptyChunk(World world)
    {
        var chunk = new Chunk(0, 0);

        for (var x = 0; x < 16; x += 4)
        {
            for (var z = 0; z < 16; z += 4)
            {
                for (var y = -64; y < 320; y += 4)
                    chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
            }
        }

        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);

        return chunk;
    }

    private static bool IsGolem(NbtCompound saved) =>
        saved.TryGetTagValue<string>("id", out var id) && id == "probe:golem";

    private static PluginManager CreateManager(RealServerContext context) => new(
        context.Server,
        context.Provider,
        context.EventDispatcher,
        context.Provider.GetRequiredService<CommandHandler>(),
        NullLogger<PluginManager>.Instance,
        new ConfigurationBuilder().Build());
}
