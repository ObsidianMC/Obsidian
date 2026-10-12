using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Entities;
using Obsidian.API.Configuration;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Plugins;
using Obsidian.API.World;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Commands.Framework;
using Obsidian.Entities;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Services;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.Loader;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

// Loading plugins merges their tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class PluginLoading : IDisposable
{
    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-plugins-{Guid.NewGuid():N}");

    public PluginLoading() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    public static TheoryData<string, string[], string[], string[], string[]> Resolutions => new()
    {
        // Name, plugins as "id version [dependency type range ordering side]*", plugins already loaded, expected load
        // order, rejected ids.
        { "required present", ["a 1.0", "b 1.0 a required [1.0,) none both"], [], ["a", "b"], [] },
        { "required too old", ["a 1.0", "b 1.0 a required [2.0,) none both"], [], ["a"], ["b"] },
        { "required before", ["a 1.0", "b 1.0 a required 1.0 before both"], [], ["a"], ["b"] },
        { "optional loads first", ["a 1.0 b optional 1.0 none both", "b 1.0"], [], ["b", "a"], [] },
        {
            "mutually optional",
            ["a 1.0 b optional 1.0 none both", "b 1.0 a optional 1.0 none both"],
            [],
            ["b", "a"],
            []
        },
        { "incompatible present", ["a 1.0", "b 1.0 a incompatible [1.0,) none both"], [], ["a"], ["b"] },
        { "loaded is incompatible", ["b 1.0"], ["a 1.0 b incompatible 1.0 none both"], [], ["b"] },
        { "cycle", ["a 1.0 b optional 1.0 after both", "b 1.0 a optional 1.0 after both"], [], [], ["a", "b"] },
        { "duplicate id", ["a 1.0", "a 2.0"], [], ["a"], ["a"] }
    };

    [Theory]
    [MemberData(nameof(Resolutions))]
    public void Resolve_FollowsNeoForgeDependencyRules(string name, string[] plugins, string[] loaded, string[] order,
        string[] rejected)
    {
        var resolution = PluginDependencyResolver.Resolve(
            [.. plugins.Select(Info)],
            [.. loaded.Select(Info)],
            PluginSide.Server);

        Assert.True(order.SequenceEqual(resolution.LoadOrder.Select(info => info.Id)), name);
        Assert.True(rejected.Order().SequenceEqual(resolution.Rejected.Keys.Select(info => info.Id).Order()), name);
    }

    [Fact]
    public void ZipArchive_ReadsMetadataAndFiles()
    {
        var files = new Dictionary<string, byte[]>
        {
            [PluginMetadata.FileName] = PluginArchives.Metadata("ruby", "1.2.0-beta", "Ruby",
                """[{ "modId": "core", "type": "optional", "versionRange": "[1.0,2.0)", "ordering": "AFTER" }]"""),
            ["Ruby.dll"] = [1, 2, 3],
            ["assets/ruby/lang/en_us.json"] = Encoding.UTF8.GetBytes("{}")
        };

        var archive = PluginArchive.Open("ruby.obby", PluginArchives.Zip(files));

        Assert.Equal("ruby", archive.Info.Id);
        Assert.Equal("ruby display", archive.Info.Name);
        Assert.Equal(new Version(1, 2, 0, 0), archive.Info.Version);
        Assert.Equal(PluginDependencyType.Optional, archive.Info.Dependencies.Single().Type);
        Assert.Equal(PluginDependencyOrdering.After, archive.Info.Dependencies.Single().Ordering);
        Assert.Equal("{}", Encoding.UTF8.GetString(archive.Read("assets/ruby/lang/en_us.json")!));
        Assert.Null(archive.Read("missing.txt"));

        // Asset and data paths are used to find files on disk too, so none may leave the archive.
        files["assets/../../evil.txt"] = [1];
        Assert.Throws<InvalidDataException>(() => PluginArchive.Open("ruby.obby", PluginArchives.Zip(files)));
    }

    [Fact]
    public async Task Reload_ReplacesHandlersAndFreesTheOldLoadContext()
    {
        // A world generating with the plugin's generator, stopped until a plugin registers it.
        var generator = new PluginLevelGenerator("probe:flat");
        var (manager, dispatcher, server) = this.CreateManager(
            TestWorldFactory.Create($"probe-{Guid.NewGuid():N}", generator));
        var path = Path.Join(this.folder, "probe.obby");

        Assert.Null(generator.Plugin);

        AppContext.SetData(ProbeGenerator.DisposedKey, 0);
        await File.WriteAllBytesAsync(path, ProbeArchive("1.0.0"));
        var (unloaded, gem, ore) = await LoadAndTrackAsync(manager, path, generator);

        await File.WriteAllBytesAsync(path, ProbeArchive("1.1.0"));
        var reloaded = await manager.LoadPluginAsync(path);

        Assert.Equal(new Version(1, 1, 0, 0), reloaded!.Info.Version);
        Assert.Single(manager.Plugins);

        // Unloading stopped the world's generator, disposing the old plugin's, and it generates again with the
        // reloaded plugin's generator, built with a scoped service. It stops when that fails, without what it wrote
        // into the chunk, and disposes it too.
        Assert.Equal(1, AppContext.GetData(ProbeGenerator.DisposedKey));
        Assert.Same(reloaded, generator.Plugin);
        Assert.Same(reloaded.LoadContext, AssemblyLoadContext.GetLoadContext(generator.Generator!.GetType().Assembly));
        Assert.True((await generator.GenerateChunkAsync(1, 0)).IsGenerated);

        var failing = new Chunk(ProbeGenerator.FailingChunk, 0);
        var failed = await generator.GenerateChunkAsync(failing.X, failing.Z, failing);

        Assert.NotSame(failing, failed);
        Assert.False(failed.IsGenerated);
        Assert.Null(generator.Plugin);
        Assert.Equal(2, AppContext.GetData(ProbeGenerator.DisposedKey));

        // The plugin's item keeps its instance and id, and gets the reloaded plugin's behaviour.
        Assert.True(ItemsRegistry.TryGet("probe:gem", out var reloadedGem));
        Assert.Same(gem, reloadedGem);
        Assert.Same(reloaded, gem.Owner);
        Assert.Equal(16, gem.MaxStackSize);

        var context = new ItemUseContext
        {
            Player = null!,
            Stack = new ItemStack(gem),
            Hand = InteractionHand.MainHand
        };
        Assert.True(await ItemBehaviors.UseAsync(context));

        // So does its block, whose item places it.
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:ore", out var reloadedOre));
        Assert.Same(ore, reloadedOre);
        Assert.Same(reloaded, ore.Owner);
        Assert.Same(ore, ore.Item?.Block);

        var blockContext = new BlockUseContext
        {
            Player = null!,
            Position = Vector.Zero,
            Block = ore.DefaultState,
            Face = BlockFace.Up,
            Hand = InteractionHand.MainHand
        };
        Assert.True(await BlockBehaviors.UseAsync(blockContext));

        // And its tags: one from its data folder, one from code.
        Assert.True(TagsRegistry.Contains("block", "probe:ores", ore.Id));
        Assert.True(TagsRegistry.Contains("block", "probe:shiny", ore.Id));

        // And its entity type, whose mobs are saved and loaded by its identifier.
        Assert.True(EntityDefinitionsRegistry.TryGet("probe:golem", out var golem));
        Assert.Same(reloaded, golem.Owner);
        Assert.True(TagsRegistry.Contains("entity_type", "probe:golems", golem.Id));

        var saved = EntityNbt.Save(new PluginMob(golem) { Level = null! })!;
        Assert.True(saved.TryGetTagValue<string>("id", out var savedId));
        Assert.Equal("probe:golem", savedId);
        Assert.Same(golem, EntityNbt.Load(saved, null!)?.Definition);

        // A projectile type's entities load as projectiles.
        Assert.True(EntityDefinitionsRegistry.TryGet("probe:pebble", out var pebble));
        var thrown = EntityNbt.Save(new PluginEntity(pebble) { Level = null! })!;
        Assert.IsType<PluginEntity>(EntityNbt.Load(thrown, null!));

        // And its worldgen types: its feature files are read with the reloaded plugin's types, and a biome placing
        // them calls into the reloaded plugin. Built here rather than read from PluginFeatures.Current, which other
        // tests' servers replace.
        var addPillar = PluginFeatures.Parse(
            PluginFeatures.BiomeModifier,
            "probe:add_pillar",
            "test",
            Encoding.UTF8.GetBytes(AddPillar));
        var features = PluginFeatures.Build(
            [.. reloaded.FeatureFiles, addPillar],
            [],
            reloaded.WorldgenTypes,
            NullLogger.Instance);
        var pillar = features.PlacedFeatures["probe:pillar"];

        Assert.Same(reloaded.LoadContext, AssemblyLoadContext.GetLoadContext(pillar.Feature.GetType().Assembly));
        Assert.Same(reloaded.LoadContext, AssemblyLoadContext.GetLoadContext(pillar.Placement[0].GetType().Assembly));
        Assert.Same(reloaded, Assert.Single(features.Owners[pillar]));

        // Only the reloaded plugin's handler runs: unloading removed the old one.
        AppContext.SetData(ProbePlugin.CallsKey, 0);
        await dispatcher.ExecuteEventAsync(new ServerStatusRequestEventArgs(server, new ServerStatus(server, "")));
        Assert.Equal(1, AppContext.GetData(ProbePlugin.CallsKey));

        // Unloading finishes over several collections, and can wait for background work like tiered compilation to
        // let go of the plugin's code, which takes longer while other tests keep the machine busy.
        for (var i = 0; unloaded.IsAlive && i < 100; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            await Task.Delay(20);
        }

        Assert.False(unloaded.IsAlive, "The unloaded plugin's load context is still referenced.");

        await manager.DisposeAsync();
        Assert.Empty(manager.Plugins);

        // Unloaded, the item loses its behaviour but stays registered with its id.
        Assert.Null(gem.Behavior);
        Assert.Same(gem, ItemsRegistry.Get(gem.Id));
        Assert.False(await ItemBehaviors.UseAsync(context));

        Assert.Null(ore.Owner);
        Assert.True(ore.Is(BlocksRegistry.Get(ore.DefaultState.GetHashCode())));
        Assert.False(await BlockBehaviors.UseAsync(blockContext));
        Assert.False(TagsRegistry.TryGet("block", "probe:ores", out _));

        // Its features are gone with its types.
        Assert.False(PluginFeatures.Current.PlacedFeatures.ContainsKey("probe:pillar"));

        // Its world stops generating, and keeps the generator's identifier to save.
        Assert.Null(generator.Plugin);
        Assert.Null(PluginGenerators.Find("probe:flat"));
        Assert.False((await generator.GenerateChunkAsync(2, 0)).IsGenerated);
        Assert.Equal("probe:flat", generator.Id);
    }

    // Adds the probe plugin's pillar to every overworld biome.
    internal const string AddPillar = """
        {
          "type": "neoforge:add_features",
          "biomes": "#minecraft:is_overworld",
          "features": "probe:pillar",
          "step": "underground_ores"
        }
        """;

    [Fact]
    public async Task UnloadFromItsOwnCall_ReleasesOnceTheCallEnds()
    {
        var (manager, _, _) = this.CreateManager();
        var path = Path.Join(this.folder, "probe.obby");

        await File.WriteAllBytesAsync(path, ProbeArchive("1.0.0"));
        var container = (await manager.LoadPluginAsync(path))!;

        // Like a plugin's command unloading the plugin: the unload can't wait for the call it runs in.
        var call = container.TryEnter()!;
        var unloading = manager.UnloadPluginAsync(container);

        Assert.Same(unloading, await Task.WhenAny(unloading, Task.Delay(TimeSpan.FromSeconds(2))));
        Assert.Empty(manager.Plugins);
        Assert.Null(container.TryEnter());
        Assert.NotNull(container.Plugin);

        call.Dispose();

        for (var i = 0; container.Plugin is not null && i < 100; i++)
            await Task.Delay(20);

        Assert.Null(container.Plugin);
    }

    // Kept out of the test method so that no local keeps the first load alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<(WeakReference LoadContext, Item Gem, BlockType Ore)> LoadAndTrackAsync(
        PluginManager manager,
        string path,
        PluginLevelGenerator generator)
    {
        var container = await manager.LoadPluginAsync(path);

        Assert.NotNull(container);
        Assert.True(container.LoadContext.IsCollectible);
        Assert.True(ItemsRegistry.TryGet("probe:gem", out var gem));
        Assert.True(gem.Id >= ItemsRegistry.VanillaCount);
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:ore", out var ore));

        // The world generates with the plugin's generator once the plugin loaded.
        Assert.Same(container, generator.Plugin);
        Assert.True((await generator.GenerateChunkAsync(0, 0)).IsGenerated);

        return (new WeakReference(container.LoadContext), gem, ore);
    }

    private (PluginManager Manager, EventDispatcher Dispatcher, FakeServer Server) CreateManager(World? world = null)
    {
        var server = world is null ? new FakeServer() : new FakeServer(world);

        var services = new ServiceCollection();
        services.AddOptions<ServerConfiguration>();
        services.AddSingleton<IServer>(server);
        var provider = services.BuildServiceProvider();

        var dispatcher = new EventDispatcher(NullLogger<EventDispatcher>.Instance, provider);
        var commands = new CommandHandler(provider, NullLogger<CommandHandler>.Instance);
        var manager = new PluginManager(server, provider, dispatcher, commands, NullLogger<PluginManager>.Instance,
            new ConfigurationBuilder().Build());

        return (manager, dispatcher, server);
    }

    // The test assembly itself, whose only plugin type is ProbePlugin, packed as a plugin.
    private static byte[] ProbeArchive(string version)
    {
        var assembly = typeof(ProbePlugin).Assembly;
        var name = assembly.GetName().Name!;

        var files = new Dictionary<string, byte[]>
        {
            [PluginMetadata.FileName] = PluginArchives.Metadata("probe", version, name),
            [$"{name}.dll"] = File.ReadAllBytes(assembly.Location)
        };

        // Its data folder, which its blocks are generated from, packed like the packer packs a plugin's.
        var output = Path.GetDirectoryName(assembly.Location)!;
        foreach (var file in Directory.EnumerateFiles(Path.Join(output, "data"), "*", SearchOption.AllDirectories))
            files[Path.GetRelativePath(output, file).Replace('\\', '/')] = File.ReadAllBytes(file);

        return PluginArchives.Zip(files);
    }

    private static PluginInfo Info(string description)
    {
        var parts = description.Split(' ');
        var dependencies = parts.Skip(2).Chunk(5).Select(dependency => new PluginDependency
        {
            Id = dependency[0],
            Type = Enum.Parse<PluginDependencyType>(dependency[1], ignoreCase: true),
            Version = dependency[2],
            Ordering = Enum.Parse<PluginDependencyOrdering>(dependency[3], ignoreCase: true),
            Side = Enum.Parse<PluginSide>(dependency[4], ignoreCase: true)
        });

        return new PluginInfo(parts[0])
        {
            Version = Version.Parse(parts[1]),
            Dependencies = [.. dependencies]
        };
    }
}

/// <summary>
/// The plugin that <see cref="PluginLoading"/> loads from the test assembly. It counts its event handler's calls in
/// <see cref="AppContext"/> data, since its static fields belong to its own copy of the assembly. Its injected services
/// have types from its own assembly, which mustn't be cached outside it.
/// </summary>
public sealed class ProbePlugin : PluginBase
{
    public const string CallsKey = "Obsidian.Tests.ProbePlugin.Calls";

    [Inject]
    public ProbeService? Service { get; set; }

    [Inject]
    public ILogger<ProbePlugin>? Logger { get; set; }

    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<ProbeService>();
        services.AddScoped<ProbeScopedService>();
    }

    public override void ConfigureRegistry(IPluginRegistry pluginRegistry)
    {
        pluginRegistry.MapEvent<ServerStatusRequestEventArgs>(_ =>
        {
            AppContext.SetData(CallsKey, (int)(AppContext.GetData(CallsKey) ?? 0) + 1);
            return ValueTask.CompletedTask;
        });

        pluginRegistry.RegisterItem("probe:gem", new ItemProperties
        {
            MaxStackSize = 16,
            Behavior = new GemBehavior()
        });

        pluginRegistry.RegisterBlock("probe:ore", new BlockProperties
        {
            StateProperties = [StateProperty.Bool("lit")],
            Behavior = new OreBehavior()
        });

        pluginRegistry.AddToTag("block", "probe:shiny", "probe:ore");
        pluginRegistry.RegisterEntity("probe:golem", new EntityProperties { Width = 1.4f, Height = 2.7f });
        pluginRegistry.AddToTag("entity_type", "probe:golems", "probe:golem");
        pluginRegistry.RegisterEntity("probe:pebble", new EntityProperties { Kind = EntityKind.Projectile });

        // Its blocks generated from data/probe/blocks.
        ProbeBlocks.Register(pluginRegistry);

        // Its worldgen types, which data/probe/worldgen's features are made of. Vanilla's identifiers, and its own
        // twice, are refused.
        pluginRegistry.RegisterFeatureType<ProbePillarFeature>("probe:pillar");
        pluginRegistry.RegisterPlacementModifierType<ProbeAtHeight>("probe:at_height");

        // Its level generator, which worlds name by its identifier.
        pluginRegistry.RegisterGenerator<ProbeGenerator>("probe:flat");

        Assert.Throws<ArgumentException>(
            () => pluginRegistry.RegisterFeatureType<ProbePillarFeature>("minecraft:pillar"));
        Assert.Throws<ArgumentException>(
            () => pluginRegistry.RegisterPlacementModifierType<ProbeAtHeight>("probe:at_height"));
    }

    private sealed class OreBehavior : IBlockBehavior
    {
        public ValueTask<bool> UseAsync(BlockUseContext context) => ValueTask.FromResult(true);
    }

    private sealed class GemBehavior : IItemBehavior
    {
        public ValueTask<bool> UseAsync(ItemUseContext context) => ValueTask.FromResult(true);
    }

    public override ValueTask OnLoadedAsync(IServer server)
    {
        // Loading injects again once the plugin's own services exist, as OnServerReadyAsync does.
        this.Container.InjectServices(NullLogger.Instance, this);

        Assert.NotNull(this.Service);
        Assert.Same(server, this.Service.Server);
        Assert.NotNull(this.Logger);

        // The generated typed states read and make the lamp's states, and its tag file was loaded with it.
        var state = new ProbeBlocks.LampState(Color: ProbeBlocks.LampColor.Green, Lit: true, Power: 2);
        Assert.True(ProbeBlocks.Lamp.Is(state.ToBlock()));
        Assert.Equal(state, ProbeBlocks.LampState.From(state.ToBlock()));
        Assert.Equal(
            new(false, 0, ProbeBlocks.LampColor.Red),
            ProbeBlocks.LampState.From(ProbeBlocks.Lamp.DefaultState));
        Assert.Equal(16, ProbeBlocks.Lamp.Item?.MaxStackSize);
        Assert.True(ProbeTags.Blocks.Ores.Contains(ProbeBlocks.Lamp.DefaultState));

        return ValueTask.CompletedTask;
    }
}

/// <summary>
/// A service of <see cref="ProbePlugin"/>, built with one of the server's services.
/// </summary>
public sealed class ProbeService(IServer server)
{
    public IServer Server { get; } = server;
}

/// <summary>
/// The behaviour of <see cref="ProbePlugin"/>'s generated lamp, named in its block definition.
/// </summary>
public sealed class ProbeLampBehavior : IBlockBehavior
{
}

/// <summary>
/// A scoped service of <see cref="ProbePlugin"/>, which its level generator is built with.
/// </summary>
public sealed class ProbeScopedService
{
}

/// <summary>
/// The probe plugin's level generator: a bedrock block in each chunk, and a failure in chunks at
/// <see cref="FailingChunk"/>. It counts its disposals in <see cref="AppContext"/> data.
/// </summary>
public sealed class ProbeGenerator(ProbeScopedService scoped) : ILevelGenerator, IDisposable
{
    public const int FailingChunk = 99;

    public const string DisposedKey = "Obsidian.Tests.ProbeGenerator.Disposed";

    public string Id => "probe:flat";

    public ProbeScopedService Scoped { get; } = scoped;

    public void Dispose() => AppContext.SetData(DisposedKey, (int)(AppContext.GetData(DisposedKey) ?? 0) + 1);

    public void Init(ILevel level)
    {
    }

    public ValueTask<IChunk> GenerateChunkAsync(int x, int z, IChunk? chunk = null,
        ChunkGenStage stage = ChunkGenStage.full)
    {
        var generated = (Chunk)(chunk ?? new Chunk(x, z));
        generated.SetBlock(0, -64, 0, BlocksRegistry.Bedrock);

        if (x == FailingChunk)
            throw new InvalidOperationException("The probe's generator failed partway.");

        generated.SetChunkStatus(ChunkGenStage.full);

        return ValueTask.FromResult<IChunk>(generated);
    }
}

/// <summary>
/// <see cref="ProbePlugin"/>'s feature type, written with the API alone: a column of a block up from its origin,
/// replacing only the blocks it may.
/// </summary>
public sealed class ProbePillarFeature : ConfiguredFeatureBase
{
    public override string Type => "probe:pillar";

    public required SimpleBlockState State { get; init; }

    public required IIntProvider Height { get; init; }

    public required BlockSet Replaceable { get; init; }

    public override bool Place(FeatureContext context)
    {
        var block = this.State.ToBlock();
        var height = this.Height.Sample(context.Random);
        var placed = false;

        for (var y = 0; y < height; y++)
        {
            var position = context.Origin + new Vector(0, y, 0);
            if (this.Replaceable.Contains(context.Level.GetBlock(position)))
                placed |= context.Level.SetBlock(position, block);
        }

        return placed;
    }
}

/// <summary>
/// <see cref="ProbePlugin"/>'s placement modifier type: moves the position to a height.
/// </summary>
public sealed class ProbeAtHeight : SinglePlacementModifierBase
{
    public override string Type => "probe:at_height";

    public required int Y { get; init; }

    public override Vector? GetPosition(PlacementContext context, IRandomSource random, Vector position) =>
        new Vector(position.X, this.Y, position.Z);
}
