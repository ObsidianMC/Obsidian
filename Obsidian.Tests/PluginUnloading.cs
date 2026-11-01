using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Commands;
using Obsidian.API.Events;
using Obsidian.API.Networking;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
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
using Obsidian.WorldData.Generators.Biomes;
using Obsidian.WorldData.Generators.Features;
using Obsidian.WorldData.Generators.Mojang;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
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

    [Trait("Category", TestCategory.Worldgen)]
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task Unload_ReleasesThe263DimensionsMadeOfItsDensityFunctions()
    {
        await using var context = TestServerFactory.CreateServer($"probe-density-{Guid.NewGuid():N}");
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        var files = new Dictionary<string, byte[]>
        {
            [ProbeOreDensity.SettingsFile] = Encoding.UTF8.GetBytes(OreWorld)
        };
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0", files));
        AppContext.SetData(ProbeOreDensity.SampledKey, null);

        // Only the ore rule names the plugin's function, which a chunk samples.
        var (loadContext, dimension, terrain) = await GenerateProbeWorldAsync(
            manager,
            path,
            "probe:ore_world",
            BlockIds.DiamondBlock);

        Assert.Equal(true, AppContext.GetData(ProbeOreDensity.SampledKey));

        await AssertStopsOnUnloadAsync(manager, dimension, terrain!);

        // Once the generator goes, nothing holds the plugin's code.
        terrain = null;
        await AssertCollectedAsync(loadContext);
    }

    [Trait("Category", TestCategory.Worldgen)]
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task Unload_ReleasesThe263DimensionsMadeOfItsSurfaceRules()
    {
        await using var context = TestServerFactory.CreateServer($"probe-crust-{Guid.NewGuid():N}");
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        var files = new Dictionary<string, byte[]>
        {
            [ProbeCrustRule.SettingsFile] = Encoding.UTF8.GetBytes(CrustWorld)
        };
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0", files));
        AppContext.SetData(ProbeCrustRule.AppliedKey, null);

        // Only the material rule names the plugin's surface rule, which a chunk applies.
        var (loadContext, dimension, terrain) = await GenerateProbeWorldAsync(
            manager,
            path,
            "probe:crust_world",
            BlockIds.GoldBlock);

        Assert.Equal(true, AppContext.GetData(ProbeCrustRule.AppliedKey));

        await AssertStopsOnUnloadAsync(manager, dimension, terrain!);

        // Once the generator goes, nothing holds the plugin's code.
        terrain = null;
        await AssertCollectedAsync(loadContext);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task Unload_ReleasesThe263FeaturesMadeOfItsBlockStateProvider()
    {
        await using var context = TestServerFactory.CreateServer($"probe-gold-{Guid.NewGuid():N}");
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        var files = new Dictionary<string, byte[]>
        {
            [ProbeGoldProvider.FeatureFile] = Encoding.UTF8.GetBytes(
                """{ "type": "minecraft:simple_block", "to_place": { "type": "probe:gold" } }"""),
            [ProbeGoldProvider.PlacedFile] = Encoding.UTF8.GetBytes("""{ "feature": "probe:gold" }""")
        };
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0", files));
        AppContext.SetData(ProbeGoldProvider.AskedKey, null);

        // Only the vanilla simple_block's provider names the plugin's type, which placing it asks.
        var loadContext = await PlaceGoldAsync(manager);

        Assert.Equal(true, AppContext.GetData(ProbeGoldProvider.AskedKey));

        // Unloading replaces the 26.3 features without the plugin's, and nothing holds its code.
        await UnloadAllAsync(manager);

        Assert.Null(PluginFeatures.CurrentRegistry.FeatureSet!.FindPlaced("probe:gold"));
        await AssertCollectedAsync(loadContext);
    }

    // Loads the plugin and places its feature with the 26.3 registry, out of the test method so that no local keeps
    // the plugin alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private async Task<WeakReference> PlaceGoldAsync(PluginManager manager)
    {
        var container = (await manager.LoadPluginAsync(Path.Join(this.folder, "probe.obby")))!;
        var registry = PluginFeatures.CurrentRegistry;
        var placed = registry.Placed("probe:gold");

        Assert.Same(container, Assert.Single(registry.OwnersOf(placed)));

        var region = PluginFeatureRegistry.Region();
        var decoration = new DecorationContext(registry, region, _ => "minecraft:plains", -64, 384);
        var origin = new Vector(8, 8, 8);

        Assert.True(placed.Place(decoration, new XoroshiroRandomSource(1), origin));
        Assert.True(region.GetBlock(origin).Is(BlockIds.GoldBlock));

        return new WeakReference(container.LoadContext);
    }

    // Unloads the plugins, and checks that the dimension was released and its generator stopped.
    private static async Task AssertStopsOnUnloadAsync(
        PluginManager manager,
        WorldgenDimension dimension,
        BiomeTerrain terrain)
    {
        await UnloadAllAsync(manager);

        Assert.True(dimension.IsReleased);
        var next = new Chunk(1, 0, terrain.MinY, terrain.Height);
        Assert.Throws<GeneratorStoppedException>(() => terrain.Generate(next));
    }

    private static async Task AssertCollectedAsync(WeakReference loadContext)
    {
        for (var i = 0; loadContext.IsAlive && i < 100; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20);
        }

        Assert.False(loadContext.IsAlive, "The unloaded plugin's load context is still referenced.");
    }

    // Loads the plugin and generates a chunk of its noise settings with the 26.3 pipeline, out of the test method so
    // that no local keeps the plugin alive. The settings make a block below y 40 the expected one.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Context, WorldgenDimension Dimension, BiomeTerrain? Terrain)>
        GenerateProbeWorldAsync(PluginManager manager, string path, string settings, int expected)
    {
        var container = (await manager.LoadPluginAsync(path))!;
        var stem = """
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "SETTINGS",
                "biome_source": { "type": "minecraft:fixed", "biome": "minecraft:plains" }
              }
            }
            """;
        var dimension = PluginWorldgen.ReadDimension(
            settings,
            Encoding.UTF8.GetBytes(stem.Replace("SETTINGS", settings)),
            PluginNoise.Current,
            PluginWorldgen.Data);

        Assert.Same(container, Assert.Single(dimension.Plugins));

        var terrain = BiomeTerrain.Create(dimension, 1234);
        var chunk = new Chunk(0, 0, terrain.MinY, terrain.Height);
        terrain.Generate(chunk);

        Assert.True(chunk.GetBlock(3, 20, 5).Is(expected));

        return (new WeakReference(container.LoadContext), dimension, terrain);
    }

    [Trait("Category", TestCategory.Worldgen)]
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task Unload_ReleasesThe263DimensionsUsingOnlyItsCarvers()
    {
        await using var context = TestServerFactory.CreateServer($"probe-carvers-{Guid.NewGuid():N}");
        await using var manager = CreateManager(context);
        var path = Path.Join(this.folder, "probe.obby");
        var files = new Dictionary<string, byte[]>
        {
            ["data/minecraft/worldgen/carver/cave.json"] =
                Encoding.UTF8.GetBytes(PluginFeatureFiles.EveryChunkCaves)
        };
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.0.0", files));

        // The stem uses vanilla's settings and biome source. Only its carver override belongs to the plugin.
        var (loadContext, dimension, terrain) = await GenerateCarverWorldAsync(manager, path);
        var before = terrain.CarversAt(0, 0);
        await UnloadAllAsync(manager);

        Assert.True(dimension.IsReleased);
        Assert.Throws<InvalidOperationException>(() => BiomeTerrain.Create(dimension, 1234));
        Assert.Throws<GeneratorStoppedException>(() =>
            terrain.Generate(new Chunk(1, 0, terrain.MinY, terrain.Height)));

        // Keep both the released dimension and the generator (including its populated carver cache) alive.
        // Their host-owned snapshots and cleared containers must not root the collectible plugin assembly.
        for (var i = 0; loadContext.IsAlive && i < 100; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20);
        }

        Assert.False(loadContext.IsAlive, "The carver-only dimension still references the unloaded plugin's code.");
        GC.KeepAlive(dimension);
        GC.KeepAlive(terrain);
        GC.KeepAlive(before);

        // A newly read dimension uses the reloaded files, without inheriting the old snapshot's configuration.
        files["data/minecraft/worldgen/carver/cave.json"] = Encoding.UTF8.GetBytes(
            PluginFeatureFiles.EveryChunkCaves.Replace("\"probability\": 1", "\"probability\": 0"));
        await File.WriteAllBytesAsync(path, PluginLoading.ProbeArchive("1.1.0", files));
        var (_, reloaded, regenerated) = await GenerateCarverWorldAsync(manager, path);
        Assert.False(reloaded.IsReleased);
        Assert.Equal(0, PluginFeatures.CurrentCarvers.Configuration("minecraft:cave").Probability);
        regenerated.Generate(new Chunk(1, 0, regenerated.MinY, regenerated.Height));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference Context, WorldgenDimension Dimension, BiomeTerrain Terrain)>
        GenerateCarverWorldAsync(PluginManager manager, string path)
    {
        var container = (await manager.LoadPluginAsync(path))!;
        var stem = """
            {
              "type": "minecraft:overworld",
              "generator": {
                "type": "minecraft:noise",
                "settings": "minecraft:overworld",
                "biome_source": { "type": "minecraft:fixed", "biome": "minecraft:plains" }
              }
            }
            """;
        var dimension = PluginWorldgen.ReadDimension(
            "probe:carvers", Encoding.UTF8.GetBytes(stem), PluginNoise.Current, PluginWorldgen.Data);
        Assert.Same(container, Assert.Single(dimension.Plugins));

        var terrain = BiomeTerrain.Create(dimension, 1234);
        terrain.Generate(new Chunk(0, 0, terrain.MinY, terrain.Height));
        return (new WeakReference(container.LoadContext), dimension, terrain);
    }

    // Solid below y 40, where the ore rule makes every block a diamond block.
    private static readonly string OreWorld = ProbeWorld("""
        {
          "type": "minecraft:ore_vein",
          "density": { "type": "probe:ore_density" },
          "richness": 1,
          "filler_gap": -1,
          "ore_block": "minecraft:diamond_block",
          "raw_ore_block": "minecraft:diamond_block",
          "filler_block": "minecraft:stone",
          "raw_ore_chance": 0.5
        }
        """);

    // Solid below y 40, where the plugin's surface rule makes every block a gold block.
    private static readonly string CrustWorld = ProbeWorld("""
        { "type": "probe:crust", "block": "minecraft:gold_block" }
        """);

    // Noise settings solid below y 40, with a material rule, in 26.3's schema.
    private static string ProbeWorld(string materialRule) => $$"""
        {
          "sea_level": -64,
          "disable_mob_generation": false,
          "legacy_random_source": false,
          "default_block": "minecraft:stone",
          "default_fluid": "minecraft:water",
          "noise": { "min_y": -64, "height": 384 },
          "noise_router": {
            "temperature": 0,
            "vegetation": 0,
            "continents": 0,
            "erosion": 0,
            "depth": 0,
            "ridges": 0,
            "chunk_surface_level": 0,
            "final_density": {
              "type": "minecraft:gradient",
              "axis": "y",
              "from_coordinate": 0,
              "to_coordinate": 80,
              "from_value": 1,
              "to_value": -1
            }
          },
          "spawn_target": [],
          "material_rule": {{materialRule}}
        }
        """;

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
