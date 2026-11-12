#nullable enable
using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Data;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class SnapshotInventoryRecovery
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ComponentGuardRejectsCallbacksAndReportsCaughtViolations(bool admission)
    {
        var component = new SimpleDataComponent<int>(DataComponentType.Damage, "minecraft:damage",
            (writer, value) => writer.WriteVarInt(value), reader => reader.ReadVarInt(), value => value)
        {
            Value = 3
        };
        var codec = new ComponentCodec<int>(value => new NbtTag<int>("", value),
            tag => ((NbtTag<int>)tag).Value);
        var type = new PluginComponentType<int>((DataComponentType)10000, "probe:guard", codec, () => new Call());
        var stack = new ItemStack(ItemsRegistry.DiamondSword);
        stack.Set(type, 3);
        using var buffer = new NetworkBuffer();
        buffer.WriteVarInt(3);
        buffer.Reset();
        Action[] callbacks =
        [
            () => _ = component.Value,
            () => component.Write(buffer),
            () => component.Read(buffer),
            () => stack.Set(type, 4),
            () => stack.Get(type)
        ];

        Assert.True(InventorySnapshot.TryCapture([], out var snapshot, out _));
        using var captured = snapshot!;
        var transfers = new EntityTransferLock();
        if (admission)
        {
            Assert.True(captured.TryEnter(out _));
            foreach (var callback in callbacks)
                ComponentCallbacks.AssertViolation(callback);
        }
        else
        {
            using (transfers.Enter())
            {
                foreach (var callback in callbacks)
                    ComponentCallbacks.AssertViolation(callback);
            }
        }
    }

    private sealed class Call : IDisposable
    {
        public void Dispose()
        {
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InventoryTimeoutKeepsChunkUsableAndLaterUnloadSavesChanges(bool wholeRegion)
    {
        await using var context = TestServerFactory.CreateServer($"review-busy-budget-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        var position = new Vector(8, 70, 8);
        await world.GetBlockAsync(position);
        var inventory = new Container(9);
        inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        var container = new ContainerBlockEntity(inventory)
        {
            Id = "probe:busy-budget",
            BlockPosition = position
        };
        chunk.SetBlockEntity(position.X, position.Y, position.Z, container);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        world.Fluids.Track(chunk);
        var attachments = AttachmentStorage.For(chunk);
        attachments.Set("probe:recovery", new NbtTag<int>("probe:recovery", 1));
        await region.FlushAsync();
        using (await inventory.LockAsync())
        {
            var unloading = Task.Run(() => wholeRegion
                ? world.UnloadRegionAsync(0, 0)
                : region.UnloadChunk(0, 0));
            await Assert.ThrowsAsync<TimeoutException>(() => unloading).WaitAsync(TimeSpan.FromSeconds(10));
            Assert.Same(chunk, world.GetLoadedChunk(0, 0));
            Assert.False(inventory.IsRetired);
            Assert.False(container.IsRemoved);
        }

        // A timeout must preserve all live chunk services, not just put the region back in its dictionary.
        await world.SetBlockAsync(position + Vector.East, BlocksRegistry.Stone);
        await world.SetBlockAsync(position + Vector.South, BlocksRegistry.Glowstone);
        world.Lighting.Flush();
        Assert.Equal(14, chunk.GetLightLevel(position.X, position.Y, position.Z, LightType.Block));
        Assert.Contains(chunk, world.Fluids.TrackedChunks());
        attachments.Set("probe:recovery", new NbtTag<int>("probe:recovery", 2));
        inventory[0] = new ItemStack(ItemsRegistry.Diamond, 5);
        await region.UnloadChunk(0, 0).WaitAsync(TimeSpan.FromSeconds(10));
        var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 5);
        Assert.Equal(BlockIds.Stone, loaded.GetBlock(position.X + 1, position.Y, position.Z).RegistryId);
        Assert.Equal(BlockIds.Glowstone, loaded.GetBlock(position.X, position.Y, position.Z + 1).RegistryId);
        var restoredAttachment = Assert.IsType<NbtTag<int>>(
            AttachmentStorage.For(loaded).Get("probe:recovery"));
        Assert.Equal(2, restoredAttachment.Value);
    }

    [Fact]
    public async Task LaterChunkTimeoutRestoresEarlierSnapshotWithoutLosingContainersOrEntities()
    {
        await using var context = TestServerFactory.CreateServer($"snapshot-region-rollback-{Guid.NewGuid():N}");
        var world = context.World;
        var first = WorldBlockEntities.AddEmptyChunk(world);
        var later = WorldBlockEntities.AddEmptyChunk(world, 1, 0);
        var position = new Vector(8, 70, 8);
        await world.GetBlockAsync(position);
        var savedContainer = AddContainer(first, position, ItemsRegistry.Diamond, 3);
        var busyContainer = AddContainer(later, position + Vector.East * 16, ItemsRegistry.Apple, 4);
        var dropped = new ItemEntity
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position,
            Item = new ItemStack(ItemsRegistry.Emerald, 2)
        };
        Assert.True(world.TrySpawnEntity(dropped, hostOwned: true));
        world.Fluids.Track(first);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        await region.FlushAsync();
        savedContainer.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 5);

        using (await busyContainer.Inventory.LockAsync())
        {
            var unloading = Task.Run(() => world.UnloadRegionAsync(0, 0));
            await Assert.ThrowsAsync<TimeoutException>(() => unloading).WaitAsync(TimeSpan.FromSeconds(10));
        }

        // The earlier chunk had already serialized and removed its entity from the grid. Rollback must give all
        // of it back before the failed unload returns, without publishing that aborted snapshot to disk.
        Assert.Same(first, world.GetLoadedChunk(0, 0));
        Assert.Same(later, world.GetLoadedChunk(1, 0));
        Assert.Same(dropped, world.FindEntity(dropped.Uuid));
        Assert.False(savedContainer.IsRemoved);
        Assert.False(savedContainer.Inventory.IsRetired);
        Assert.Contains(first, world.Fluids.TrackedChunks());
        var prior = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        SnapshotInventoryLocking.AssertStack(prior, position, ItemsRegistry.Diamond, 3);

        await world.SetBlockAsync(position + Vector.East, BlocksRegistry.Glowstone);
        world.Lighting.Flush();
        Assert.Equal(14, first.GetLightLevel(position.X, position.Y, position.Z, LightType.Block));
        AttachmentStorage.For(first).Set("probe:recovered", new NbtTag<int>("probe:recovered", 7));
        AttachmentStorage.For(dropped).Set("probe:recovered", new NbtTag<int>("probe:recovered", 8));
        savedContainer.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 6);
        dropped.Item.Count = 3;
        await region.UnloadChunksAsync([(0, 0), (1, 0)]).WaitAsync(TimeSpan.FromSeconds(10));

        var restored = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        SnapshotInventoryLocking.AssertStack(restored, position, ItemsRegistry.Diamond, 6);
        var pending = Assert.Single(restored.PendingEntities);
        var item = Assert.IsType<ItemEntity>(EntityNbt.Load(EntityNbt.ToNbt(pending), world));
        Assert.True(item.Item.Is(ItemsRegistry.Emerald));
        Assert.Equal(3, item.Item.Count);
        Assert.Equal(8, Assert.IsType<NbtTag<int>>(AttachmentStorage.For(item).Get("probe:recovered")).Value);
        var restoredLater = await SnapshotInventoryLocking.ReloadDiskAsync(region, 1, 0);
        SnapshotInventoryLocking.AssertStack(restoredLater, position + Vector.East * 16, ItemsRegistry.Apple, 4);
    }

    private static ContainerBlockEntity AddContainer(Chunk chunk, Vector position, Item item, int count)
    {
        var container = new ContainerBlockEntity(new Container(9))
        {
            Id = "probe:rollback",
            BlockPosition = position
        };
        container.Inventory[0] = new ItemStack(item, count);
        chunk.SetBlockEntity(position.X, position.Y, position.Z, container);
        return container;
    }
}
