using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Plugins;
using Obsidian.Commands.Framework;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Services;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

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
        { "required missing", ["b 1.0 a required [1.0,) none both"], [], [], ["b"] },
        { "required too old", ["a 1.0", "b 1.0 a required [2.0,) none both"], [], ["a"], ["b"] },
        { "required before", ["a 1.0", "b 1.0 a required 1.0 before both"], [], ["a"], ["b"] },
        {
            "rejection cascades",
            ["b 1.0 a required 1.0 none both", "c 1.0 b required 1.0 none both"],
            [],
            [],
            ["b", "c"]
        },
        { "optional absent", ["b 1.0 a optional 1.0 none both"], [], ["b"], [] },
        { "optional wrong version", ["a 1.0", "b 1.0 a optional [2.0,) none both"], [], ["a"], ["b"] },
        { "optional loads first", ["a 1.0 b optional 1.0 none both", "b 1.0"], [], ["b", "a"], [] },
        {
            "mutually optional",
            ["a 1.0 b optional 1.0 none both", "b 1.0 a optional 1.0 none both"],
            [],
            ["b", "a"],
            []
        },
        { "incompatible present", ["a 1.0", "b 1.0 a incompatible [1.0,) none both"], [], ["a"], ["b"] },
        { "incompatible other version", ["a 1.0", "b 1.0 a incompatible [2.0,) none both"], [], ["a", "b"], [] },
        { "loaded is incompatible", ["b 1.0"], ["a 1.0 b incompatible 1.0 none both"], [], ["b"] },
        { "loaded needs another version", ["b 1.0"], ["a 1.0 b optional [2.0,) none both"], [], ["b"] },
        { "discouraged only warns", ["a 1.0", "b 1.0 a discouraged 1.0 none both"], [], ["a", "b"], [] },
        { "ordered before", ["a 1.0", "b 1.0 a optional 1.0 before both"], [], ["b", "a"], [] },
        { "client-only dependency", ["b 1.0 a required 1.0 none client"], [], ["b"], [] },
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
    public void LegacyArchive_ReadsHeaderOrModJson()
    {
        var files = new Dictionary<string, byte[]> { ["Old.dll"] = [4, 5, 6] };

        var legacy = PluginArchive.Open("old.obby", PluginArchives.Legacy("old", "1.0.0", "Old", files));

        Assert.Equal("old", legacy.Info.Id);
        Assert.Equal("Old", legacy.Info.AssemblyName);
        Assert.Equal([4, 5, 6], legacy.Read("Old.dll"));

        files[PluginMetadata.FileName] = PluginArchives.Metadata("renamed", "2.0.0", "Old");
        var withMetadata = PluginArchive.Open("old.obby", PluginArchives.Legacy("old", "1.0.0", "Old", files));

        Assert.Equal("renamed", withMetadata.Info.Id);
        Assert.Equal(new Version(2, 0, 0, 0), withMetadata.Info.Version);
    }

    [Fact]
    public async Task Reload_ReplacesHandlersAndFreesTheOldLoadContext()
    {
        var (manager, dispatcher, server) = this.CreateManager();
        var path = Path.Join(this.folder, "probe.obby");

        await File.WriteAllBytesAsync(path, ProbeArchive("1.0.0"));
        var (unloaded, gem) = await LoadAndTrackAsync(manager, path);

        await File.WriteAllBytesAsync(path, ProbeArchive("1.1.0"));
        var reloaded = await manager.LoadPluginAsync(path);

        Assert.Equal(new Version(1, 1, 0, 0), reloaded!.Info.Version);
        Assert.Single(manager.Plugins);

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
    }

    [Fact]
    public void PluginItems_StackOnlyWithThemselves()
    {
        // Plugin items have no vanilla material, so they must be told apart by identity.
        var ruby = ItemsRegistry.Register("stacking:ruby");
        var sapphire = ItemsRegistry.Register("stacking:sapphire");
        var container = new Container(9);

        Assert.Equal(0, container.AddItem(new ItemStack(ruby, 1)));
        Assert.Equal(1, container.AddItem(new ItemStack(sapphire, 1)));
        Assert.Equal(0, container.AddItem(new ItemStack(ruby, 1)));

        Assert.Same(ruby, container.GetItem(0)!.Holder);
        Assert.Equal(2, container.GetItem(0)!.Count);
        Assert.Same(sapphire, container.GetItem(1)!.Holder);
        Assert.Same(ruby, ItemsRegistry.Register("stacking:ruby"));
    }

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
    private static async Task<(WeakReference LoadContext, Item Gem)> LoadAndTrackAsync(PluginManager manager,
        string path)
    {
        var container = await manager.LoadPluginAsync(path);

        Assert.NotNull(container);
        Assert.True(container.LoadContext.IsCollectible);
        Assert.True(ItemsRegistry.TryGet("probe:gem", out var gem));
        Assert.True(gem.Id >= ItemsRegistry.VanillaCount);

        return (new WeakReference(container.LoadContext), gem);
    }

    private (PluginManager Manager, EventDispatcher Dispatcher, FakeServer Server) CreateManager()
    {
        var server = new FakeServer();

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

        return PluginArchives.Zip(new Dictionary<string, byte[]>
        {
            [PluginMetadata.FileName] = PluginArchives.Metadata("probe", version, name),
            [$"{name}.dll"] = File.ReadAllBytes(assembly.Location)
        });
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

    public override void ConfigureServices(IServiceCollection services) => services.AddSingleton<ProbeService>();

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
