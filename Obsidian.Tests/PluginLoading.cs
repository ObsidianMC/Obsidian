using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Events;
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
using System.Security.Cryptography;
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
    public void ZipArchive_ReadsMetadataFilesAndSignature()
    {
        using var key = RSA.Create(2048);
        using var otherKey = RSA.Create(2048);

        var files = new Dictionary<string, byte[]>
        {
            [PluginMetadata.FileName] = PluginArchives.Metadata("ruby", "1.2.0-beta", "Ruby",
                """[{ "modId": "core", "type": "optional", "versionRange": "[1.0,2.0)", "ordering": "AFTER" }]"""),
            ["Ruby.dll"] = [1, 2, 3],
            ["assets/ruby/lang/en_us.json"] = Encoding.UTF8.GetBytes("{}")
        };
        var data = PluginArchives.Zip(files, key);

        var archive = PluginArchive.Open("ruby.obby", data, [key.ExportParameters(false)]);

        Assert.True(archive.ValidSignature);
        Assert.Equal("ruby", archive.Info.Id);
        Assert.Equal("ruby display", archive.Info.Name);
        Assert.Equal(new Version(1, 2, 0, 0), archive.Info.Version);
        Assert.Equal(PluginDependencyType.Optional, archive.Info.Dependencies.Single().Type);
        Assert.Equal(PluginDependencyOrdering.After, archive.Info.Dependencies.Single().Ordering);
        Assert.Equal("{}", Encoding.UTF8.GetString(archive.Read("assets/ruby/lang/en_us.json")!));
        Assert.Null(archive.Read("missing.txt"));

        Assert.False(PluginArchive.Open("ruby.obby", data, [otherKey.ExportParameters(false)]).ValidSignature);

        // Changing a file after signing invalidates the signature.
        files["Ruby.dll"] = [9];
        files[ZipPluginArchive.SignatureFileName] = archive.Read(ZipPluginArchive.SignatureFileName)!;
        var tampered = PluginArchives.Zip(files);
        Assert.False(PluginArchive.Open("ruby.obby", tampered, [key.ExportParameters(false)]).ValidSignature);
    }

    [Fact]
    public void LegacyArchive_ReadsHeaderOrModJson()
    {
        var files = new Dictionary<string, byte[]> { ["Old.dll"] = [4, 5, 6] };

        var legacy = PluginArchive.Open("old.obby", PluginArchives.Legacy("old", "1.0.0", "Old", files), []);

        Assert.Equal("old", legacy.Info.Id);
        Assert.Equal("Old", legacy.Info.AssemblyName);
        Assert.Equal([4, 5, 6], legacy.Read("Old.dll"));
        Assert.False(legacy.ValidSignature);

        files[PluginMetadata.FileName] = PluginArchives.Metadata("renamed", "2.0.0", "Old");
        var withMetadata = PluginArchive.Open("old.obby", PluginArchives.Legacy("old", "1.0.0", "Old", files), []);

        Assert.Equal("renamed", withMetadata.Info.Id);
        Assert.Equal(new Version(2, 0, 0, 0), withMetadata.Info.Version);
    }

    [Fact]
    public async Task Reload_ReplacesHandlersAndFreesTheOldLoadContext()
    {
        var (manager, dispatcher, server) = this.CreateManager();
        var path = Path.Join(this.folder, "probe.obby");

        await File.WriteAllBytesAsync(path, ProbeArchive("1.0.0"));
        var unloaded = await LoadAndTrackAsync(manager, path);

        await File.WriteAllBytesAsync(path, ProbeArchive("1.1.0"));
        var reloaded = await manager.LoadPluginAsync(path);

        Assert.Equal(new Version(1, 1, 0, 0), reloaded!.Info.Version);
        Assert.Single(manager.Plugins);

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
    }

    // Kept out of the test method so that no local keeps the first load alive.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static async Task<WeakReference> LoadAndTrackAsync(PluginManager manager, string path)
    {
        var container = await manager.LoadPluginAsync(path);

        Assert.NotNull(container);
        Assert.True(container.LoadContext.IsCollectible);

        return new WeakReference(container.LoadContext);
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

    public override void ConfigureRegistry(IPluginRegistry pluginRegistry) =>
        pluginRegistry.MapEvent<ServerStatusRequestEventArgs>(_ =>
        {
            AppContext.SetData(CallsKey, (int)(AppContext.GetData(CallsKey) ?? 0) + 1);
            return ValueTask.CompletedTask;
        });

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
