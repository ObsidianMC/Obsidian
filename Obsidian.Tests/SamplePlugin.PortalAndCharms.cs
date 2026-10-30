using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.API;
using Obsidian.API.Configuration;
using Obsidian.API.Inventory;
using Obsidian.API.Networking;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Net;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Generators;
using System;
using System.Threading.Tasks;
using Xunit;
using Dimension = Obsidian.WorldData.Dimension;

namespace Obsidian.Tests;

public sealed partial class SamplePlugin
{
    [Fact(DisplayName = "Phase 5: a lit amethyst frame takes players to the sample's dimension, and their charm slots "
        + "take charms alone, each granting a jump", Timeout = TestServerFactory.TestTimeout)]
    public async Task PortalAndCharms()
    {
        await using var context = TestServerFactory.CreateServer($"sample-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        world.LevelData.GameRules["spawn_mobs"] = "false";
        var player = await WorldBlockEntities.JoinAsync(context);

        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = await this.PackSampleAsync();
        var plugin = (await manager.LoadPluginAsync(archive))!;
        Assert.NotNull(plugin);

        // A server creates the dimension's level as the plugin loads (see WorldManager.AddPluginDimensionsAsync),
        // which test servers don't: the level of its stem, a flat one of the sample's type, here with empty chunks.
        var stem = Assert.IsType<FlatStem>(PluginDimensions.Find("sample:dusk"));
        Assert.Equal(0, stem.DimensionType.Element.MinY);
        Assert.Equal("minecraft:the_end", stem.DimensionType.Element.DefaultClock);
        Assert.Equal("#minecraft:in_end", stem.DimensionType.Element.Timelines);
        Assert.Equal("sample:dusk", Assert.Single(stem.SourceBiomes).Name);

        var spawn = Assert.Single(Assert.Single(stem.SourceBiomes).Element.Spawners["creature"]);
        Assert.Equal("sample:moth", spawn.Type);
        Assert.NotNull(spawn.Count);

        await using var dusk = new Dimension(
            NullLogger<Dimension>.Instance,
            context.Broadcaster,
            new TestOptionsMonitor<ServerConfiguration>(world.Configuration),
            context.EventDispatcher,
            new EmptyWorldGenerator(),
            stem.Identifier,
            world);
        dusk.Initialize(stem.DimensionType);
        world.RegisterDimension(dusk);
        for (var x = -3; x <= 3; x++)
        {
            for (var z = -3; z <= 3; z++)
                dusk.LoadRegionByChunk(x, z).SetChunk(new Chunk(x, z, dusk.MinY, dusk.Height, ChunkGenStage.full));
        }

        // An amethyst frame two wide and three high, its bottom in the floor, lit by using a shard on its bottom.
        var amethyst = BlocksRegistry.AmethystBlock;
        var floor = new Vector(3, BlockUpdates.Ground, 8);
        for (var x = 0; x < 2; x++)
        {
            await world.SetBlockAsync(floor + new Vector(x, 0, 0), amethyst);
            await world.SetBlockAsync(floor + new Vector(x, 4, 0), amethyst);
        }

        for (var y = 1; y <= 3; y++)
        {
            await world.SetBlockAsync(floor + new Vector(-1, y, 0), amethyst);
            await world.SetBlockAsync(floor + new Vector(2, y, 0), amethyst);
        }

        player.GameMode = GameMode.Survival;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.AmethystShard, 2));
        await BlockUpdates.UseOn(floor, BlockFace.Up).HandleAsync(context.Server, player);

        Assert.Equal(1, player.GetHeldItem()?.Count);
        Assert.True(BlocksRegistry.TryGetPluginBlock("sample:dusk_portal", out var portal));
        Assert.True(portal.Is(await world.GetBlockAsync(floor + Vector.Up)));

        // Standing in it takes the player to the same place in the dimension, where an exit is built.
        player.Position = (VectorD)(floor + Vector.Up) + new VectorD(0.5, 0, 0.5);
        for (var tick = 0; tick < 120 && player.Level == world; tick++)
        {
            await player.TickPortalsAsync();
            await Task.Delay(5);
        }

        Assert.Same(dusk, player.Level);
        Assert.True(portal.Is(await dusk.GetBlockAsync((Vector)player.Position.Floor())));

        // The charm key's payload opens the player's charm slots, which take charms alone, one in each.
        Assert.True(context.Server.PayloadChannels.TryGet("sample:open_charms", out var open));
        var sent = new PayloadContext
        {
            Client = player.Client,
            Player = player,
            Server = context.Server,
            Phase = PayloadPhase.Play
        };
        await open.Handler!(new NetworkBuffer(), sent);
        var menu = Assert.IsType<MenuContainer>(player.OpenedContainer, exactMatch: false);
        Assert.Equal("sample:charms", menu.Menu.Identifier);

        Assert.True(ItemsRegistry.TryGet("sample:charm", out var charm));
        player.Inventory.SetItem(9, new ItemStack(charm, 1));
        player.Inventory.SetItem(10, new ItemStack(ItemsRegistry.Dirt, 1));
        await MenuClicks.ClickAsync(context.Server, player, 2, 0, ClickType.QuickMove);
        await MenuClicks.ClickAsync(context.Server, player, 3, 0, ClickType.QuickMove);

        Assert.Null(player.Inventory.GetItem(9));
        Assert.True(player.Inventory.GetItem(10)?.Is(ItemsRegistry.Dirt));
        Assert.True(menu.GetItem(0)?.Is(charm));
        Assert.Equal(1, player.MovementAbilities.ExtraJumps);

        // Saved charms survive unload, while movement grants do not. Loading reapplies them to online players.
        await manager.UnloadPluginAsync(plugin);
        Assert.Equal(0, player.MovementAbilities.ExtraJumps);
        Assert.NotNull(await manager.LoadPluginAsync(archive));
        Assert.Equal(1, player.MovementAbilities.ExtraJumps);
        // The old payload binding belongs to the unloaded generation; use the new binding to reopen its menu.
        Assert.True(context.Server.PayloadChannels.TryGet("sample:open_charms", out open));
        await open.Handler!(new NetworkBuffer(), sent);
        menu = Assert.IsType<MenuContainer>(player.OpenedContainer, exactMatch: false);
        Assert.True(menu.GetItem(0)?.Is(charm));

        // Taking the charm off takes the jump back.
        await MenuClicks.ClickAsync(context.Server, player, 0, 0, ClickType.QuickMove);

        Assert.Null(menu.GetItem(0));
        Assert.Equal(0, player.MovementAbilities.ExtraJumps);
    }
}
