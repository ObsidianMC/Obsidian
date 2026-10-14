using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Payloads;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The engine sample plugin (samples/EngineSample), packed from its build output and loaded through the plugin
/// manager like any plugin: one smoke test per phase of docs/engine-foundations.md, through what a player does.
/// </summary>
// Loading plugins merges their tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class SamplePlugin : IDisposable
{
    private readonly string folder = Path.Join(Path.GetTempPath(), $"obsidian-sample-{Guid.NewGuid():N}");

    public SamplePlugin() => Directory.CreateDirectory(this.folder);

    public void Dispose() => Directory.Delete(this.folder, recursive: true);

    [Fact(DisplayName = "Phase 1: the tick counter faces its placer, counts while powered, lights, reloads and drops",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task TickCounter()
    {
        await using var context = TestServerFactory.CreateServer($"sample-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        world.LevelData.GameRules["spawn_mobs"] = "false";
        var chunk = world.GetLoadedChunk(0, 0)!;

        // A player north of where the block goes, looking west, who watches the chunk.
        var player = TestServerFactory.CreateConnectedPlayer(
            context.Server,
            world,
            "Sampler",
            new VectorD(4.5, BlockUpdates.Ground + 1, 2));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        player.LoadedChunks.Add(NumericsHelper.IntsToLong(0, 0));
        player.GameMode = GameMode.Survival;
        player.Yaw = 90;
        var client = (TestClient)player.Client;

        await using var manager = BlockUpdates.CreatePluginManager(context);
        var path = Path.Join(this.folder, "sample.obby");
        var output = Path.Join(AppContext.BaseDirectory, "EngineSample");
        await File.WriteAllBytesAsync(path, PluginArchives.Folder(output));
        var plugin = await manager.LoadPluginAsync(path);
        Assert.NotNull(plugin);

        Assert.True(BlocksRegistry.TryGetPluginBlock("sample:tick_counter", out var counter));
        Assert.True(BlockEntityTypesRegistry.TryGet("sample:tick_counter", out var type));
        Assert.Equal([counter], type.Blocks);
        var synced = SyncedRegistries.Get(SyncedRegistries.BlockEntityTypes);
        Assert.Contains((type.Id, "sample:tick_counter"), synced.Entries);
        Assert.Contains(SyncedRegistries.GetModded(), registry => registry.Name == SyncedRegistries.BlockEntityTypes);

        // Placed on the ground, it faces the player, unpowered and dark, with its block entity and its shape.
        var position = new Vector(4, BlockUpdates.Ground + 1, 4);
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(counter.Item!, 1));
        await BlockUpdates.UseOn(position + Vector.Down, BlockFace.Up).HandleAsync(context.Server, player);

        var placed = chunk.GetBlock(position);
        Assert.Equal("east", counter.GetValue(placed, "facing"));
        Assert.Equal("false", counter.GetValue(placed, "powered"));
        Assert.Equal(0, chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Block));
        Assert.Equal("TickCounterBlockEntity", chunk.GetBlockEntity(4, position.Y, 4)?.GetType().Name);
        Assert.Equal(0.75, BlockEntityShapes.Get(world, position, placed)!.Collision.Single().Max.Y);

        // A block of redstone west of it powers it a scheduled tick later, which lights it; a comparator east of it
        // reads the seconds it counts into the dust beyond.
        IBlock Vanilla(string name, params (string Name, string Value)[] properties) =>
            BlockStateProperties.GetState($"minecraft:{name}", properties.ToDictionary(p => p.Name, p => p.Value));

        var comparator = Vanilla("comparator", ("facing", "west"));
        var dust = position + Vector.East * 2;
        await world.SetBlockAsync(position + Vector.East, comparator, BlockUpdateFlags.All);
        await world.SetBlockAsync(dust, Vanilla("redstone_wire"), BlockUpdateFlags.All);
        await world.SetBlockAsync(position + Vector.West, Vanilla("redstone_block"), BlockUpdateFlags.All);

        for (var tick = 0; tick < 4; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal("true", counter.GetValue(chunk.GetBlock(position), "powered"));
        Assert.Equal(10, chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Block));
        Assert.Equal(9, chunk.GetLightLevel(position.X, position.Y + 1, position.Z, LightType.Block));

        // It counts each tick, and sends watching players the count each second.
        var counted = Ticks(chunk, position);
        client.ClearPackets();
        for (var tick = 0; tick < 50; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal(counted + 50, Ticks(chunk, position));
        var update = client.SentPackets.OfType<BlockEntityDataPacket>().Last(packet => packet.Position == position);
        Assert.Equal(type.Id, update.Type);
        Assert.Equal(0, update.NBTData.GetInt("ticks") % 20);
        Assert.Equal($"{Ticks(chunk, position) / 20}", chunk.GetBlock(dust).GetProperty("power"));

        // Unloading the plugin keeps the block entity as it saved; loading it again brings it back, counting on.
        var before = Assert.IsAssignableFrom<BlockEntity>(chunk.GetBlockEntity(4, position.Y, 4));
        await manager.UnloadPluginAsync(plugin);

        Assert.True(before.IsRemoved);
        var kept = Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(4, position.Y, 4));
        Assert.Equal(Ticks(chunk, position), kept.Data.GetInt("ticks"));

        counted = Ticks(chunk, position);
        await manager.LoadPluginAsync(path);
        await world.DoWorldTickAsync();

        var restored = chunk.GetBlockEntity(4, position.Y, 4)!;
        Assert.Equal("TickCounterBlockEntity", restored.GetType().Name);
        Assert.NotSame(before.GetType(), restored.GetType());
        Assert.Equal(counted + 1, Ticks(chunk, position));

        // Without power it goes dark and stops counting.
        await world.SetBlockAsync(position + Vector.West, BlocksRegistry.Air, BlockUpdateFlags.All);
        for (var tick = 0; tick < 4; tick++)
            await world.DoWorldTickAsync();

        counted = Ticks(chunk, position);
        await world.DoWorldTickAsync();

        Assert.Equal("false", counter.GetValue(chunk.GetBlock(position), "powered"));
        Assert.Equal(0, chunk.GetLightLevel(position.X, position.Y + 1, position.Z, LightType.Block));
        Assert.Equal(counted, Ticks(chunk, position));

        // Broken by the player looking at it, it drops itself through its loot table.
        player.Yaw = 0;
        player.Pitch = 32;
        var state = chunk.GetBlock(position);
        await context.EventDispatcher.ExecuteEventAsync(
            new BlockBreakEventArgs(context.Server, player, state, position, world));
        await world.DoWorldTickAsync();

        Assert.True(chunk.GetBlock(position).IsAir);
        Assert.Null(chunk.GetBlockEntity(4, position.Y, 4));
        var drop = Assert.Single(world.GetNonPlayerEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>()).Item;
        Assert.True(drop.Is(counter.Item!));
        Assert.Equal(1, drop.Count);

        // The removed block entity's OnRemoved runs on the next tick, which its plugin waits for before it's released.
        await world.DoWorldTickAsync();
    }

    // The ticks the block entity at position counted, as it saved them at the end of the last tick.
    private static int Ticks(IChunk chunk, Vector position) =>
        BlockEntityNbt.Save(chunk.GetBlockEntity(position.X, position.Y, position.Z)!)!.GetInt("ticks");
}
