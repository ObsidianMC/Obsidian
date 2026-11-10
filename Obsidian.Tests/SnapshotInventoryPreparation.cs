#nullable enable

using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

// Eager saves deliberately skip when the shared staging slots are full. Run these bounded eager checks alone.
[CollectionDefinition(nameof(SnapshotInventoryPreparation), DisableParallelization = true)]
public sealed class SnapshotPreparationCollection;

[Collection(nameof(SnapshotInventoryPreparation))]
public sealed class SnapshotInventoryPreparation
{
    private static readonly Vector position = new(8, 70, 8);
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);

    [Theory]
    [InlineData("save")]
    [InlineData("unload")]
    [InlineData("eager")]
    public async Task ReadOnlyNestedCaptureCompletesAndSavesBothContainers(string operation)
    {
        await using var context = TestServerFactory.CreateServer($"snapshot-readonly-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var first = AddContainer(chunk, position, new CallbackInventory());
        var second = AddContainer(chunk, position + Vector.East, new CallbackInventory());
        first.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        second.Inventory[0] = new ItemStack(ItemsRegistry.Apple, 5);
        var ordered = chunk.BlockEntities.Values.OfType<AuxiliaryContainer>().ToArray();
        var earlier = ordered[0];
        var later = Assert.IsType<CallbackInventory>(ordered[1].Inventory);
        later.OnRead = earlier.Capture;
        var region = (Region)world.LoadRegionByChunk(0, 0);
        await region.InitAsync();
        region.BeforeInventorySnapshot = () =>
        {
            // Also exercise an actual read-only lock after capture: cached auxiliary reads alone must not
            // conceal a regression that invalidates snapshots merely for taking/releasing a lock.
            Assert.True(first.Inventory.TryLock(out var reading));
            using (reading)
                Assert.Equal(3, first.Inventory.GetItem(0)?.Count);
        };

        var saving = Task.Run(() => SaveAsync(region, chunk, operation));
        try
        {
            // The nested read stays enabled until the real save/unload has completed. No retry counter or
            // scheduling assumption can make an endlessly invalidated snapshot pass this check.
            try
            {
                await saving.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                Assert.Fail("A read-only auxiliary save kept invalidating the snapshot instead of completing.");
            }

            var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
            SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 3);
            SnapshotInventoryLocking.AssertStack(loaded, position + Vector.East, ItemsRegistry.Apple, 5);
        }
        finally
        {
            later.OnRead = null;
            region.BeforeInventorySnapshot = null;
            await saving.WaitAsync(timeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistentPreparationMutationFailsWithoutRetirementAndALaterSaveRecovers(bool unload)
    {
        await using var context = TestServerFactory.CreateServer($"snapshot-changing-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var inventory = new CallbackInventory();
        var container = AddContainer(chunk, position, inventory);
        inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        await region.FlushAsync();
        var nextCount = 3;
        inventory.OnRead = () => inventory[0] = new ItemStack(ItemsRegistry.Diamond, ++nextCount);
        var operation = unload ? "unload" : "save";
        var saving = Task.Run(() => SaveAsync(region, chunk, operation));
        try
        {
            try
            {
                await Assert.ThrowsAsync<TimeoutException>(() => saving).WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Persistent inventory mutation never exhausted the snapshot retry budget.");
            }
            Assert.Same(chunk, world.GetLoadedChunk(0, 0));
            Assert.False(container.IsRemoved);
            Assert.False(inventory.IsRetired);
            Assert.True(chunk.IsUnsaved);
            var prior = await SnapshotInventoryLocking.ReloadDiskAsync(region);
            SnapshotInventoryLocking.AssertStack(prior, position, ItemsRegistry.Diamond, 3);
        }
        finally
        {
            inventory.OnRead = null;
            await saving.WaitAsync(timeout).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
        }

        inventory[0] = new ItemStack(ItemsRegistry.Diamond, 5);
        await SaveAsync(region, chunk, operation).WaitAsync(timeout);
        var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 5);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComponentCopyDelegatesRunBeforeAdmissionAndPersistAcrossSnapshotSerializers(bool unload)
    {
        await using var context = TestServerFactory.CreateServer($"snapshot-copy-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var foreign = new EntityTransferLock();
        var auxiliary = new AuxiliaryContainer(new Container(9));
        auxiliary.Inventory[0] = new ItemStack(ItemsRegistry.Apple, 2);
        var damage = Damage(() =>
        {
            using (foreign.Enter())
            {
            }

            auxiliary.Capture();
        });
        var container = AddContainer(chunk, position, new Container(9));
        container.Inventory[0] = new ItemStack(ItemsRegistry.DiamondSword, 1, damage);
        var bookPosition = position + Vector.North;
        var lectern = new LecternBlockEntity(bookPosition)
        {
            Id = "minecraft:lectern",
            BlockPosition = bookPosition
        };
        lectern.Inventory[0] = new ItemStack(ItemsRegistry.WritableBook, 1, damage);
        chunk.SetBlockEntity(bookPosition.X, bookPosition.Y, bookPosition.Z, lectern);
        var boat = new ChestBoat
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position
        };
        boat.Inventory[0] = new ItemStack(ItemsRegistry.DiamondSword, 1, damage);
        Assert.True(world.TrySpawnEntity(boat, hostOwned: true));
        Assert.True(world.TrySpawnEntity(new ItemEntity
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position,
            Item = new ItemStack(ItemsRegistry.DiamondSword, 1, damage)
        }, hostOwned: true));
        var region = (Region)world.LoadRegionByChunk(0, 0);

        await SaveAsync(region, chunk, unload ? "unload" : "save").WaitAsync(timeout);
        var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.DiamondSword, 1);
        var kept = Assert.IsType<DataBlockEntity>(loaded.GetBlockEntity(position.X, position.Y, position.Z));
        var restored = Assert.IsAssignableFrom<ContainerBlockEntity>(kept.KeptContainer());
        Assert.Equal(1, restored.Inventory[0]?.Damage);
        var restoredLectern = Assert.IsType<LecternBlockEntity>(
            loaded.GetBlockEntity(bookPosition.X, bookPosition.Y, bookPosition.Z));
        Assert.True(restoredLectern.Lectern.Book?.Is(ItemsRegistry.WritableBook));
        Assert.Equal(1, restoredLectern.Lectern.Book?.Count);
        Assert.Equal(1, restoredLectern.Lectern.Book?.Damage);
        var savedBoat = Assert.Single(loaded.PendingEntities,
            entity => EntityNbt.Load(EntityNbt.ToNbt(entity), world) is ChestBoat);
        var dropped = Assert.Single(loaded.PendingEntities,
            entity => EntityNbt.Load(EntityNbt.ToNbt(entity), world) is ItemEntity);
        var restoredDrop = Assert.IsType<ItemEntity>(EntityNbt.Load(EntityNbt.ToNbt(dropped), world));
        Assert.True(restoredDrop.Item.Is(ItemsRegistry.DiamondSword));
        Assert.Equal(1, restoredDrop.Item.Count);
        Assert.Equal(1, restoredDrop.Item.Damage);
        var restoredBoat = Assert.IsType<ChestBoat>(EntityNbt.Load(EntityNbt.ToNbt(savedBoat), world));
        Assert.Equal(1, restoredBoat.Inventory[0]?.Damage);
        Assert.True(restoredBoat.Inventory[0]?.Is(ItemsRegistry.DiamondSword));
        Assert.Single(restoredBoat.Inventory, item => item is not null);
    }

    [Fact]
    public async Task BusyAuxiliaryCopyDefersBeforeWaitingAndLetsWorldWritesFinish()
    {
        await using var context = TestServerFactory.CreateServer($"snapshot-copy-busy-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var auxiliary = new AuxiliaryContainer(new Container(9));
        auxiliary.Inventory[0] = new ItemStack(ItemsRegistry.Apple, 2);
        var container = AddContainer(chunk, position, new Container(9));
        container.Inventory[0] = new ItemStack(ItemsRegistry.DiamondSword, 1, Damage(auxiliary.Capture));
        var region = (Region)world.LoadRegionByChunk(0, 0);
        var deferred = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        region.InventorySnapshotDeferred = () => deferred.TrySetResult();
        var held = await auxiliary.Inventory.LockAsync();
        var saving = Task.Run(() => region.FlushAsync());
        Task writingBlock = Task.CompletedTask;
        var editedPosition = position + Vector.North;
        try
        {
            try
            {
                await deferred.Task.WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Snapshot waited for an auxiliary inventory under tick instead of deferring.");
            }

            // Keep the auxiliary inventory held: a world write must finish before the snapshot may acquire it.
            writingBlock = Task.Run(async () => await world.SetBlockAsync(editedPosition, BlocksRegistry.Stone));
            await writingBlock.WaitAsync(timeout);
        }
        finally
        {
            held.Dispose();
            region.InventorySnapshotDeferred = null;
            await Task.WhenAll(saving, writingBlock).WaitAsync(timeout);
        }

        var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.DiamondSword, 1);
        Assert.Equal(BlockIds.Stone,
            loaded.GetBlock(editedPosition.X, editedPosition.Y, editedPosition.Z).RegistryId);
    }

    [Fact]
    public async Task ReplacedEntityStackIsRecapturedBeforeAnyUnloadRetirement()
    {
        await using var context = TestServerFactory.CreateServer($"snapshot-entity-stack-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var container = AddContainer(chunk, position, new Container(9));
        container.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        var dropped = new ItemEntity
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position,
            Item = new ItemStack(ItemsRegistry.Apple, 1)
        };
        Assert.True(world.TrySpawnEntity(dropped, hostOwned: true));
        var region = (Region)world.LoadRegionByChunk(0, 0);
        region.BeforeInventorySnapshot = () =>
        {
            region.BeforeInventorySnapshot = null;
            dropped.Item = new ItemStack(ItemsRegistry.Apple, 5);
        };

        await region.UnloadChunk(0, 0).WaitAsync(timeout);
        var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 3);
        var saved = Assert.Single(loaded.PendingEntities);
        var restored = Assert.IsType<ItemEntity>(EntityNbt.Load(EntityNbt.ToNbt(saved), world));
        Assert.True(restored.Item.Is(ItemsRegistry.Apple));
        Assert.Equal(5, restored.Item.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ComponentCopyFailureLeavesThePriorSaveIntactAndCanBeRetried(bool unload)
    {
        await using var context = TestServerFactory.CreateServer($"snapshot-copy-failure-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var first = AddContainer(chunk, position, new Container(9));
        var second = AddContainer(chunk, position + Vector.East, new Container(9));
        first.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        var failing = false;
        var damage = Damage(() =>
        {
            if (failing)
                throw new InvalidOperationException("Component copy failure.");
        });
        second.Inventory[0] = new ItemStack(ItemsRegistry.DiamondSword, 1, damage);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        await region.FlushAsync();
        first.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 5);
        failing = true;
        try
        {
            var saving = SaveAsync(region, chunk, unload ? "unload" : "save");
            await Assert.ThrowsAsync<InvalidOperationException>(() => saving).WaitAsync(timeout);
            Assert.Same(chunk, world.GetLoadedChunk(0, 0));
            Assert.False(first.Inventory.IsRetired);
            Assert.False(second.Inventory.IsRetired);
            var prior = await SnapshotInventoryLocking.ReloadDiskAsync(region);
            SnapshotInventoryLocking.AssertStack(prior, position, ItemsRegistry.Diamond, 3);
        }
        finally
        {
            failing = false;
        }

        await SaveAsync(region, chunk, unload ? "unload" : "save").WaitAsync(timeout);
        var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 5);
        SnapshotInventoryLocking.AssertStack(loaded, position + Vector.East, ItemsRegistry.DiamondSword, 1);
    }

    private static AuxiliaryContainer AddContainer(Chunk chunk, Vector at, BaseContainer inventory)
    {
        var container = new AuxiliaryContainer(inventory)
        {
            Id = "probe:prepared",
            BlockPosition = at
        };
        chunk.SetBlockEntity(at.X, at.Y, at.Z, container);
        return container;
    }

    private static async Task SaveAsync(Region region, Chunk chunk, string operation)
    {
        switch (operation)
        {
            case "save":
                await region.FlushAsync();
                break;
            case "unload":
                await region.UnloadChunk(0, 0);
                break;
            case "eager":
                var selected = new List<IChunk>();
                var taken = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                var writing = region.TrySaveChunks([chunk], selected, taken);
                Assert.NotNull(writing);
                await writing;
                Assert.Contains(chunk, selected);
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(operation));
        }
    }

    private static SimpleDataComponent<int> Damage(Action onCopy) => new(
        DataComponentType.Damage,
        "minecraft:damage",
        (_, _) => { },
        _ => 0,
        value =>
        {
            onCopy();
            return value;
        }) { Value = 1 };

    private sealed class AuxiliaryContainer(BaseContainer inventory) : ContainerBlockEntity(inventory)
    {
        internal void Capture() => base.Save(new NbtCompound());
    }

    private sealed class CallbackInventory() : BaseContainer(9)
    {
        internal Action? OnRead { get; set; }

        public override ItemStack? GetItem(int slot)
        {
            this.OnRead?.Invoke();
            return base.GetItem(slot);
        }
    }
}
