using Obsidian.API;
using Obsidian.API.Events;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plugins' particle and menu types: numbered after vanilla's, synced to modded clients, and sent with their ids; and
/// their menus closing as the plugin unloads.
/// </summary>
public sealed class PluginParticlesAndMenus
{
    [Fact(DisplayName = "A plugin's particle and menu types get ids after vanilla's, are synced and sent by id")]
    public void IdsFollowVanillasAndAreSynced()
    {
        var spark = ParticleTypesRegistry.Register("particles:spark");
        var pouch = MenuTypesRegistry.Register("particles:pouch", 2, 5);

        // After every vanilla entry of the registries clients know, and the same again when a plugin reloads.
        Assert.Equal("minecraft:firefly", ParticleTypesRegistry.VanillaIdentifiers[(int)ParticleType.Firefly]);
        Assert.True(spark.Id >= ParticleTypesRegistry.VanillaIdentifiers.Count);
        Assert.True(pouch.Id >= MenuTypesRegistry.VanillaIdentifiers.Count);
        Assert.Same(spark, ParticleTypesRegistry.Register("particles:spark"));
        Assert.Same(pouch, MenuTypesRegistry.Register("particles:pouch", 2, 5));
        Assert.True(MenuTypesRegistry.TryGet(pouch.Id, out var byId) && byId == pouch);

        // A menu's shape numbers its slots on clients, so it stays; vanilla's namespace isn't a plugin's.
        Assert.Throws<ArgumentException>(() => MenuTypesRegistry.Register("particles:pouch", 3, 5));
        Assert.Throws<ArgumentOutOfRangeException>(() => MenuTypesRegistry.Register("particles:huge", 7, 9));
        Assert.Throws<ArgumentException>(() => ParticleTypesRegistry.Register("minecraft:spark"));

        // NeoForge and Fabric clients are sent both registries, vanilla's entries and the plugin's.
        var modded = SyncedRegistries.GetModded().ToDictionary(registry => registry.Name);
        Assert.Contains((0, "minecraft:angry_villager"), modded[SyncedRegistries.ParticleTypes].Entries);
        Assert.Contains((spark.Id, "particles:spark"), modded[SyncedRegistries.ParticleTypes].Entries);
        Assert.Contains((0, "minecraft:generic_9x1"), modded[SyncedRegistries.Menus].Entries);
        Assert.Contains((pouch.Id, "particles:pouch"), modded[SyncedRegistries.Menus].Entries);

        // The particle is sent by its id, without options, and the menu opens by its id.
        var particles = Written(new LevelParticlesPacket
        {
            Position = VectorD.Zero,
            ParticleCount = 4,
            Data = spark
        });
        Assert.Equal(Write(writer => writer.WriteVarInt(spark.Id)), particles[^VarIntSize(spark.Id)..]);

        var container = new MenuContainer(pouch, "Pouch");
        var screen = Written(new OpenScreenPacket(container, 3));
        Assert.Equal(10, container.Size);
        Assert.Equal(
            Write(writer =>
            {
                writer.WriteVarInt(3);
                writer.WriteVarInt(pouch.Id);
            }),
            screen[..(1 + VarIntSize(pouch.Id))]);
    }

    [Fact(DisplayName = "Particles reach players closer than their range in 3D, from their block's centre")]
    public void ParticleRangeIsThreeDimensional()
    {
        var player = new VectorD(0.9, 100, 0.9);

        Assert.False(AbstractLevel.IsInParticleRange(player, new VectorD(0.5, 64, 0.5), 32));
        Assert.True(AbstractLevel.IsInParticleRange(player, new VectorD(0.5, 69, 0.5), 32));
        Assert.True(AbstractLevel.IsInParticleRange(player, new VectorD(0.5, 64, 0.5), 512));
    }

    [Fact(DisplayName = "An unloading plugin sees its menus close, which then don't open until it loads again",
        Timeout = 10000)]
    public async Task UnloadingClosesMenusForGood()
    {
        await using var context = TestServerFactory.CreateServer($"menu-unload-{Guid.NewGuid():N}");
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Viewer");
        context.Server.OnlinePlayers.TryAdd(player.Uuid, player);

        var plugin = Plugin();
        var bag = PluginRegistry.RegisterMenu(plugin, "unloadprobe:bag", 1, 9);
        var closes = 0;
        context.EventDispatcher.RegisterEvent(plugin, (Func<ContainerClosedEventArgs, ValueTask>)(_ =>
        {
            closes++;
            return ValueTask.CompletedTask;
        }));

        await player.OpenInventoryAsync(new MenuContainer(bag, "Bag"));

        // Like unloading: the plugin takes no more calls, but its close handler still sees its menu close.
        plugin.Close();
        await PluginManager.CloseMenusAsync(context.Server, plugin);

        Assert.Equal(1, closes);
        Assert.Null(player.OpenedContainer);
        Assert.Single(((TestClient)player.Client).SentPackets.OfType<ContainerClosePacket>());

        // Its calls still running can't open the type's menus again, nor register it.
        await player.OpenInventoryAsync(new MenuContainer(bag, "Bag"));
        Assert.Null(player.OpenedContainer);
        Assert.Throws<InvalidOperationException>(() => PluginRegistry.RegisterMenu(plugin, "unloadprobe:bag", 1, 9));

        // Once the plugin loads again, they open.
        Assert.Same(bag, PluginRegistry.RegisterMenu(Plugin(), "unloadprobe:bag", 1, 9));
        await player.OpenInventoryAsync(new MenuContainer(bag, "Bag"));
        Assert.IsType<MenuContainer>(player.OpenedContainer);

        static PluginContainer Plugin() => new()
        {
            Info = new PluginInfo("unloadprobe"),
            Archive = PluginArchive.Open(
                "unloadprobe.obby",
                PluginArchives.Zip(new Dictionary<string, byte[]>
                {
                    [PluginMetadata.FileName] = PluginArchives.Metadata("unloadprobe", "1.0.0", "Unload probe")
                }))
        };
    }

    private static int VarIntSize(int value) => Write(writer => writer.WriteVarInt(value)).Length;

    private static byte[] Written(IClientboundPacket packet) => Write(packet.Serialize);

    private static byte[] Write(Action<INetStreamWriter> write)
    {
        using var buffer = new NetworkBuffer();
        write(buffer);

        return buffer.AsSpan(0, buffer.Size).ToArray();
    }
}
