using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.API.Events;
using Obsidian.API.Networking;
using Obsidian.API.Registries;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Entities.AI;
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
using System.Runtime.CompilerServices;
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
        string[] keys =
        [
            ProbePlugin.BatKey, ProbePlugin.BotKey, ProbePlugin.ServerKey, ProbePlugin.BossBarKey, ProbePlugin.PouchClosesKey
        ];

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
    public async Task Unload_ClosesServerPlayersMenusAndLetsGoOfThePlugin()
    {
        await using var context = TestServerFactory.CreateServer($"probe-menu-{Guid.NewGuid():N}");
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));

        // A server-side player the host added, which outlives the plugin, has one of its menus open.
        var player = await context.Server.AddServerPlayerAsync(Guid.NewGuid(), "HostBot", context.World);
        var loadContext = await LoadAndOpenPouchAsync(manager, path, player);
        Assert.IsType<MenuContainer>(player.OpenedContainer, exactMatch: false);

        await UnloadAllAsync(manager);

        // The plugin's handler saw its menu close, and nothing holds its code any more.
        Assert.Equal(1, AppContext.GetData(ProbePlugin.PouchClosesKey));
        Assert.Null(player.OpenedContainer);
        Assert.True(context.Server.IsPlayerOnline(player.Uuid));

        for (var i = 0; loadContext.IsAlive && i < 100; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20);
        }

        Assert.False(loadContext.IsAlive, "The unloaded plugin's load context is still referenced.");
    }

    // Kept out of the test method so that no local keeps the plugin alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> LoadAndOpenPouchAsync(PluginManager manager, string path, IPlayer player)
    {
        var container = (await manager.LoadPluginAsync(path))!;
        var openPouch = container.Plugin!.GetType().GetMethod(nameof(ProbePlugin.OpenPouchAsync))!;
        await (ValueTask)openPouch.Invoke(container.Plugin, [player])!;

        return new WeakReference(container.LoadContext);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task UnloadAllAsync(PluginManager manager)
    {
        foreach (var plugin in manager.Plugins.ToArray())
            await manager.UnloadPluginAsync(plugin);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task Reload_InsidePluginCall_PreservesOldGenerationState()
    {
        await using var context = TestServerFactory.CreateServer($"probe-reload-{Guid.NewGuid():N}");
        var chunk = AddEmptyChunk(context.World);
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var old = (await manager.LoadPluginAsync(path))!;
        Assert.True(EntityDefinitionsRegistry.TryGet("probe:golem", out var definition));

        // One entity is plugin-credited; the other follows the chunk-loading path with no spawning plugin.
        Entity credited;
        using (old.TryEnter())
            credited = (Entity)context.World.SpawnEntity(position, definition);

        credited.Health = 13;
        credited.CustomName = "credited";
        var saved = EntityNbt.Save(credited)!;

        // A copy of it: without the saved UUID, which another entity in the level has, it gets one of its own.
        saved.Remove("UUID");
        var restored = EntityNbt.Load(saved, context.World)!;
        restored.CustomName = "restored";
        Assert.True(context.World.TrySpawnEntity(restored, hostOwned: true));
        Assert.Null(restored.SpawnedBy);

        using (old.TryEnter())
            Assert.NotNull(await manager.LoadPluginAsync(path));

        // An entity of the replacement generation must survive the old generation's delayed cleanup.
        var replacement = context.World.SpawnEntity(position, definition);
        await context.World.DoWorldTickAsync();

        var entities = context.World.Regions.Values.SelectMany(region => region.Entities.Values).ToList();
        foreach (var original in new[] { credited, restored })
        {
            var respawned = Assert.Single(entities, entity => entity.Uuid == original.Uuid);
            Assert.NotSame(original, respawned);
            Assert.Equal(13, respawned.Health);
            Assert.Equal(original.CustomName, respawned.CustomName);
        }

        Assert.Contains(replacement, entities);
        Assert.DoesNotContain(chunk.UnspawnableEntities, IsGolem);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task Unload_DismountsRidersAndClearsHostAiReferences()
    {
        await using var context = TestServerFactory.CreateServer($"probe-rider-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        var human = await JoinAsync(context);
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var plugin = (await manager.LoadPluginAsync(path))!;

        Horse horse;
        using (plugin.TryEnter())
            horse = (Horse)context.World.SpawnEntity(position, EntityType.Horse);

        await horse.FeedAsync(human, InteractionHand.MainHand);
        Assert.Same(horse, human.Vehicle);
        var zombie = (Zombie)context.World.SpawnEntity(position, EntityType.Zombie);
        zombie.AttackTarget = horse;
        zombie.AlertedTarget = horse;
        var navigator = Assert.IsType<Navigator>(zombie.Navigator);
        navigator.NavigateTo(horse);
        var client = (TestClient)human.Client;
        client.ClearPackets();

        await manager.UnloadPluginAsync(plugin);

        Assert.Null(human.Vehicle);
        Assert.True(horse.IsRemoved);
        Assert.Null(horse.GoalController);
        Assert.Null(horse.Navigator);
        Assert.Null(zombie.AttackTarget);
        Assert.Null(zombie.AlertedTarget);
        Assert.Null(navigator.Target);
        Assert.False(navigator.IsNavigating);
        Assert.Contains(client.SentPackets.OfType<SetPassengersPacket>(),
            packet => packet.EntityId == horse.EntityId && packet.Passengers.Length == 0);
        Assert.Contains(client.SentPackets.OfType<RemoveEntitiesPacket>(),
            packet => packet.Entities.Contains(horse.EntityId));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task BackgroundWork_KeepsOwnershipAfterCallAndRejectsSpawningAfterUnload()
    {
        await using var context = TestServerFactory.CreateServer($"probe-background-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        var human = await JoinAsync(context);
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var plugin = (await manager.LoadPluginAsync(path))!;
        var chat = new IncomingChatMessageEventArgs(human, context.Server, "hello", "");
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var spawned = new TaskCompletionSource<IEntity>(TaskCreationOptions.RunContinuationsAsynchronously);
        var unloaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Task background;
        using (plugin.TryEnter())
        {
            background = Task.Run(async () =>
            {
                await resume.Task;
                Assert.Same(plugin.HostServices.Server, chat.Server);
                spawned.SetResult(context.World.SpawnEntity(position, EntityType.Squid));
                await unloaded.Task;
                Assert.Throws<InvalidOperationException>(
                    () => context.World.SpawnEntity(position, EntityType.Squid));
            });
        }

        resume.SetResult();
        var entity = await spawned.Task;
        await manager.UnloadPluginAsync(plugin);
        unloaded.SetResult();
        await background;
        Assert.DoesNotContain(entity, context.World.Regions.Values.SelectMany(region => region.Entities.Values));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task Unload_DuringJoin_RollsBackLateLevelRegistration()
    {
        await using var context = TestServerFactory.CreateServer($"probe-join-{Guid.NewGuid():N}");
        AddEmptyChunk(context.World);
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0"));
        var plugin = (await manager.LoadPluginAsync(path))!;
        var entered = new TaskCompletionSource<IPlayer>(TaskCreationOptions.RunContinuationsAsynchronously);
        var resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        context.EventDispatcher.RegisterEvent(null, (Func<PlayerJoinEventArgs, ValueTask>)(async args =>
        {
            entered.SetResult(args.Player);
            await resume.Task;
        }));

        var join = plugin.HostServices.Server.AddServerPlayerAsync(Guid.NewGuid(), "JoiningBot", context.World);
        var player = await entered.Task;
        await manager.UnloadPluginAsync(plugin);
        resume.SetResult();
        await Assert.ThrowsAsync<InvalidOperationException>(() => join);

        Assert.False(context.Server.IsPlayerOnline(player.Uuid));
        Assert.DoesNotContain(player.Uuid, context.World.Players.Keys);
        Assert.DoesNotContain(player, context.World.Regions.Values.SelectMany(region => region.Entities.Values));
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
