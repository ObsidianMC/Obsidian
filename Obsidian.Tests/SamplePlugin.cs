using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Nbt;
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

    [Fact(DisplayName = "Phase 1: the tick counter faces its placer, counts while powered, lights, reloads and drops "
        + "with its count", Timeout = TestServerFactory.TestTimeout)]
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
        var path = await this.PackSampleAsync();
        var plugin = await manager.LoadPluginAsync(path);
        Assert.NotNull(plugin);

        var listener = TestServerFactory.CreateConnectedPlayer(
            context.Server, world, "Listener", player.Position + new VectorD(1, 0, 0));
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, listener);
        var listening = (TestClient)listener.Client;
        listening.ClearPackets();

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
        var placementSound = Assert.Single(listening.SentPackets.OfType<SoundPacket>());
        Assert.Equal("sample:counter.chime", placementSound.SoundLocation);
        Assert.Equal(24, placementSound.FixedRange);
        Assert.Equal(0.85f, placementSound.Volume);
        Assert.Equal(0.96f, placementSound.Pitch, precision: 5);
        Assert.Empty(client.SentPackets.OfType<SoundPacket>());
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

        Assert.False(SoundEventsRegistry.TryGet("sample:counter.chime", out _));
        Assert.Equal(BlockSoundProperties.Stone, counter.SoundType);

        Assert.True(before.IsRemoved);
        var kept = Assert.IsType<DataBlockEntity>(chunk.GetBlockEntity(4, position.Y, 4));
        Assert.Equal(Ticks(chunk, position), kept.Data.GetInt("ticks"));

        counted = Ticks(chunk, position);
        await manager.LoadPluginAsync(path);
        await world.DoWorldTickAsync();

        Assert.True(SoundEventsRegistry.TryGet("sample:counter.chime", out var chime));
        Assert.Equal(chime, counter.SoundType.Break);

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

        // Serial tick operations explicitly collect inline, even after an await. A child task receives no ambient
        // permission from its parent and must wait for the simulation gate.
        var blockEntity = Assert.IsAssignableFrom<BlockEntity>(chunk.GetBlockEntity(4, position.Y, 4));
        var owner = Assert.IsType<Obsidian.Plugins.PluginContainer>(blockEntity.Owner);
        Task? child = null;
        world.EnqueueEntityAction(async () =>
        {
            await Task.Yield();
            Assert.NotEmpty(await world.CollectPluginComponentsAsync(
                blockEntity, owner, counter.Item!, onLevelTick: true));
            var waiting = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            child = Task.Run(async () =>
            {
                await Task.Yield();
                var collection = world.CollectPluginComponentsAsync(blockEntity, owner, counter.Item!);
                var completedOnTick = collection.IsCompleted;
                waiting.SetResult();
                Assert.False(completedOnTick);
                Assert.NotEmpty(await collection);
            });
            await waiting.Task;
        });
        await world.DoWorldTickAsync();
        await child!;

        // Broken by the player looking at it, on the level's tick, it drops itself through its loot table, which
        // copies the count its block entity gave the item.
        player.Yaw = 0;
        player.Pitch = 32;
        var state = chunk.GetBlock(position);
        listening.ClearPackets();
        await context.EventDispatcher.ExecuteEventAsync(
            new BlockBreakEventArgs(context.Server, player, state, position, world));
        await world.DoWorldTickAsync();

        Assert.True(chunk.GetBlock(position).IsAir);
        var breakSound = Assert.Single(listening.SentPackets.OfType<SoundPacket>());
        Assert.Equal("sample:counter.chime", breakSound.SoundLocation);
        Assert.Equal(24, breakSound.FixedRange);
        Assert.Equal(0.85f, breakSound.Volume);
        Assert.Equal(0.96f, breakSound.Pitch, precision: 5);
        Assert.Empty(client.SentPackets.OfType<SoundPacket>());
        Assert.Null(chunk.GetBlockEntity(4, position.Y, 4));
        var dropped = Assert.Single(world.GetNonPlayerEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>());
        var drop = dropped.Item;
        Assert.True(drop.Is(counter.Item!));
        Assert.Equal(1, drop.Count);
        Assert.Equal(counted, CountOnItem(drop));

        // Placed again, it reads the count back and saves it.
        await dropped.RemoveAsync();
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(drop, 1));
        await BlockUpdates.UseOn(position + Vector.Down, BlockFace.Up).HandleAsync(context.Server, player);
        await world.DoWorldTickAsync();

        Assert.Equal(counted, Ticks(chunk, position));

        // An explosion off the level's tick queues the plugin break, including collection, to the next tick.
        world.LevelData.GameRules["block_explosion_drop_decay"] = "false";
        await world.ExplodeAsync((VectorD)position + 0.5, 2, false, AbstractLevel.ExplosionInteraction.Block);

        Assert.False(chunk.GetBlock(position).IsAir);
        await world.DoWorldTickAsync();

        Assert.True(chunk.GetBlock(position).IsAir);
        var blown = Assert.Single(
            world.GetNonPlayerEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>(),
            item => item.Item.Is(counter.Item!));
        Assert.Equal(counted, CountOnItem(blown.Item));

        // The same explosion called from a tick queues collection without waiting on the gate the tick holds.
        await blown.RemoveAsync();
        await world.SetBlockAsync(position, state, BlockUpdateFlags.All);
        world.EnqueueEntityAction(async () =>
        {
            await BlockEntityComponents.ApplyAsync(world, position, blown.Item);
            await world.ExplodeAsync((VectorD)position + 0.5, 2, false, AbstractLevel.ExplosionInteraction.Block);
        });
        await world.DoWorldTickAsync();

        Assert.False(chunk.GetBlock(position).IsAir);
        await world.DoWorldTickAsync();

        Assert.True(chunk.GetBlock(position).IsAir);
        var tickDrop = Assert.Single(
            world.GetNonPlayerEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>(),
            item => item.Item.Is(counter.Item!));
        Assert.Equal(counted, CountOnItem(tickDrop.Item));

        // The removed block entity's OnRemoved runs on the next tick, which its plugin waits for before it's released.
        await world.DoWorldTickAsync();
    }

    [Fact(DisplayName = "Phase 2: the grinder grinds ore from a chest above into a hopper below, losing nothing",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task Grinder()
    {
        await using var context = TestServerFactory.CreateServer($"sample-{Guid.NewGuid():N}");
        var world = context.World;
        await BlockUpdates.AddChunksAsync(world);
        world.LevelData.GameRules["spawn_mobs"] = "false";

        // A player nearby, so that the chunk ticks.
        await WorldBlockEntities.JoinAsync(context);

        await using var manager = BlockUpdates.CreatePluginManager(context);
        Assert.NotNull(await manager.LoadPluginAsync(await this.PackSampleAsync()));
        Assert.True(BlocksRegistry.TryGetPluginBlock("sample:grinder", out var grinder));

        // A hopper on the ground, the grinder on it and a chest on top, holding a stone block, which doesn't grind,
        // before three iron ore.
        var hopper = new Vector(4, BlockUpdates.Ground + 1, 4);
        var machine = hopper + Vector.Up;
        var chest = machine + Vector.Up;
        await world.SetBlockAsync(hopper, BlocksRegistry.Hopper);
        await world.SetBlockAsync(machine, grinder.DefaultState);
        await world.SetBlockAsync(chest, BlocksRegistry.Chest);

        var chestItems = world.GetItemAccess(chest, side: null)!;
        Assert.Null(chestItems.Insert(0, new ItemStack(ItemsRegistry.Stone, 1)));
        Assert.Null(chestItems.Insert(1, new ItemStack(ItemsRegistry.IronOre, 3)));

        // It pulls an ore a tick, past the stone, and grinds one at a time, saving how far it got.
        for (var tick = 0; tick < 10; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal(0, Count(world, chest, ItemsRegistry.IronOre));
        Assert.Equal(3, Count(world, machine, ItemsRegistry.IronOre));
        Assert.Equal(10, Saved(world, machine).GetInt("progress"));

        // Its recipe, data/sample/recipe/grinding/iron_ore.json, grinds each ore into two raw iron over 20 ticks,
        // which it pushes into the hopper, stamped with where they were ground. Nothing is lost or made twice.
        for (var tick = 10; tick < 70; tick++)
            await world.DoWorldTickAsync();

        Assert.Equal(1, Count(world, chest, ItemsRegistry.Stone));
        Assert.Equal(6, Count(world, hopper, ItemsRegistry.RawIron));
        Assert.Equal(0, Count(world, chest, ItemsRegistry.RawIron) + Count(world, machine, ItemsRegistry.RawIron));
        Assert.All(
            [chest, machine, hopper],
            position => Assert.Equal(0, Count(world, position, ItemsRegistry.IronOre)));
        Assert.Equal(0, Saved(world, machine).GetInt("progress"));

        var ground = world.GetItemAccess(hopper, side: null)!.GetItem(0)!.ToNbt();
        Assert.True(ground.TryGetTag<NbtCompound>("components", out var components));
        Assert.True(components.TryGetTag<NbtArray<int>>("sample:ground_by", out var groundBy));
        Assert.Equal([machine.X, machine.Y, machine.Z], groundBy.GetArray());
    }

    // The sample's build output, packed into a plugin archive as Obsidian.MSBuild packs it.
    private async Task<string> PackSampleAsync()
    {
        var path = Path.Join(this.folder, "sample.obby");
        var output = Path.Join(AppContext.BaseDirectory, "EngineSample");
        await File.WriteAllBytesAsync(path, PluginArchives.Folder(output));

        return path;
    }

    // How many of item the block at position holds, through its item access from no particular side.
    private static int Count(World world, Vector position, Item item)
    {
        var access = world.GetItemAccess(position, side: null)!;

        return Enumerable.Range(0, access.SlotCount)
            .Select(access.GetItem)
            .Where(stack => stack is not null && stack.Is(item))
            .Sum(stack => stack!.Count);
    }

    // What the block entity at position saved at the end of the last tick.
    private static NbtCompound Saved(World world, Vector position) =>
        BlockEntityNbt.Save(world.GetBlockEntityAsync(position).AsTask().GetAwaiter().GetResult()!)!;

    // The tick counter's count on its item, in sample:ticks.
    private static int CountOnItem(ItemStack item)
    {
        Assert.True(item.ToNbt().TryGetTag<NbtCompound>("components", out var components));

        return components.GetInt("sample:ticks");
    }

    // The ticks the block entity at position counted, as it saved them at the end of the last tick.
    private static int Ticks(IChunk chunk, Vector position) =>
        BlockEntityNbt.Save(chunk.GetBlockEntity(position.X, position.Y, position.Z)!)!.GetInt("ticks");
}
