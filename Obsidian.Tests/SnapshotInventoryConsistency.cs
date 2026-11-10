#nullable enable

using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Nbt;
using Obsidian.Entities;
using Obsidian.Plugins;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class SnapshotInventoryConsistency
{
    private static readonly Vector position = new(8, 70, 8);
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);

    [Fact]
    public async Task DeferredKeepPreservesChangedPluginFields()
    {
        await using var context = TestServerFactory.CreateServer($"review-keep-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = Path.Join(Path.GetDirectoryName(world.PlayerDataPath)!, "review.obby");
        await File.WriteAllBytesAsync(archive, PluginLoading.ProbeArchive("1.0.0"));
        var plugin = Assert.IsType<PluginContainer>(await manager.LoadPluginAsync(archive));

        var entity = new CounterEntity
        {
            Id = "probe:counter",
            BlockPosition = position,
            Owner = plugin,
            Counter = 1
        };
        entity.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        entity.SavedData = BlockEntityNbt.SaveData(entity);
        entity.SavedRevision = entity.Inventory.Revision;
        chunk.SetBlockEntity(position.X, position.Y, position.Z, entity);
        world.AttachBlockEntity(entity);
        plugin.Close();

        // Only the plugin state changes; a read-only inventory transaction blocks admission.
        entity.Counter = 2;
        entity.SetChanged();
        var held = await entity.Inventory.LockAsync();
        try
        {
            await Task.Run(() => PluginBlockEntities.Keep(world, chunk, entity, plugin)).WaitAsync(timeout);
            Assert.Same(entity, world.GetResidentBlockEntity(position));
        }
        finally
        {
            held.Dispose();
        }

        await world.RunQueuedEntityActionsAsync().WaitAsync(timeout);
        var kept = Assert.IsType<DataBlockEntity>(world.GetResidentBlockEntity(position));
        Assert.Equal(2, kept.Data.GetInt("counter"));
        var region = (Region)world.LoadRegionByChunk(0, 0);
        await region.FlushAsync().WaitAsync(timeout);
        var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
        var restored = Assert.IsType<DataBlockEntity>(loaded.GetBlockEntity(position.X, position.Y, position.Z));
        Assert.Equal(2, restored.Data.GetInt("counter"));
        SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 3);
    }

    [Fact]
    public async Task DisposingLevelReleasesQueuedPreservationHold()
    {
        await using var context = TestServerFactory.CreateServer($"review-hold-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = Path.Join(Path.GetDirectoryName(world.PlayerDataPath)!, "review-hold.obby");
        await File.WriteAllBytesAsync(archive, PluginLoading.ProbeArchive("1.0.0"));
        var plugin = Assert.IsType<PluginContainer>(await manager.LoadPluginAsync(archive));
        var entity = new CounterEntity
        {
            Id = "probe:counter",
            BlockPosition = position,
            Owner = plugin,
            Counter = 1
        };
        entity.SavedData = BlockEntityNbt.SaveData(entity);
        entity.SavedRevision = entity.Inventory.Revision;
        chunk.SetBlockEntity(position.X, position.Y, position.Z, entity);
        world.AttachBlockEntity(entity);
        plugin.Close();

        lock (world.Fluids.TickLock)
            PluginBlockEntities.Keep(world, chunk, entity, plugin);

        await world.DisposeAsync().AsTask().WaitAsync(timeout);
        var idle = plugin.WaitForCallsAsync();
        try
        {
            Assert.True(idle.IsCompleted, "Disposed level retained a lease requiring a future tick.");
        }
        finally
        {
            // Failure cleanup also joins the old implementation: only disposal, above, may satisfy the assertion.
            await world.RunQueuedEntityActionsAsync().WaitAsync(timeout);
            await idle.WaitAsync(timeout);
        }
    }

    [Fact]
    public async Task SnapshotDoesNotAddForeignTransferLockToVirtualInventoryGetter()
    {
        await using var context = TestServerFactory.CreateServer($"review-getter-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var foreignTransfer = new EntityTransferLock();
        var inventory = new CallbackInventory
        {
            OnRead = () =>
            {
                using (foreignTransfer.Enter())
                {
                }
            }
        };
        inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        chunk.SetBlockEntity(position.X, position.Y, position.Z, new ContainerBlockEntity(inventory)
        {
            Id = "probe:virtual",
            BlockPosition = position
        });

        try
        {
            var region = (Region)world.LoadRegionByChunk(0, 0);
            await region.FlushAsync().WaitAsync(timeout);
            var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
            SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 3);
        }
        finally
        {
            inventory.OnRead = null;
        }
    }

    [Fact]
    public async Task SnapshotDoesNotRejectAnotherInventoryReachedByVirtualGetter()
    {
        await using var context = TestServerFactory.CreateServer($"review-admission-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var auxiliary = new AuxiliaryContainer();
        var inventory = new CallbackInventory
        {
            OnRead = auxiliary.Capture
        };
        inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        chunk.SetBlockEntity(position.X, position.Y, position.Z, new ContainerBlockEntity(inventory)
        {
            Id = "probe:virtual",
            BlockPosition = position
        });

        try
        {
            var region = (Region)world.LoadRegionByChunk(0, 0);
            await region.FlushAsync().WaitAsync(timeout);
            var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
            SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 3);
        }
        finally
        {
            inventory.OnRead = null;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task VehicleMovingIntoChunkAfterAdmissionDoesNotFailSnapshot(bool unload)
    {
        await using var context = TestServerFactory.CreateServer($"review-moving-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        WorldBlockEntities.AddEmptyChunk(world, 1, 0);
        await world.GetBlockAsync(position);
        var boat = new ChestBoat
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = new VectorD(24, 70, 8)
        };
        boat.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        Assert.True(world.TrySpawnEntity(boat, hostOwned: true));
        Assert.True(world.TrySpawnEntity(new ItemEntity
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position,
            Item = new ItemStack(ItemsRegistry.Apple, 1)
        }, hostOwned: true));

        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var moved = new ManualResetEventSlim();
        var inventory = new CallbackInventory();
        inventory.OnRead = () =>
        {
            inventory.OnRead = null;
            admitted.TrySetResult();
            Assert.True(moved.Wait(timeout), "The public position setter blocked behind snapshot locks.");
        };
        chunk.SetBlockEntity(position.X, position.Y, position.Z, new ContainerBlockEntity(inventory)
        {
            Id = "probe:rendezvous",
            BlockPosition = position
        });

        var moving = Task.Run(async () =>
        {
            await admitted.Task.WaitAsync(timeout);
            boat.Position = (VectorD)position;
            moved.Set();
        });
        try
        {
            var region = (Region)world.LoadRegionByChunk(0, 0);
            await Task.Run(() => unload ? region.UnloadChunk(0, 0) : region.FlushAsync()).WaitAsync(timeout);
            var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
            var savedBoat = Assert.Single(loaded.PendingEntities,
                entity => EntityNbt.Load(EntityNbt.ToNbt(entity), world) is ChestBoat);
            var restored = Assert.IsType<ChestBoat>(EntityNbt.Load(EntityNbt.ToNbt(savedBoat), world));
            Assert.True(restored.Inventory[0]?.Is(ItemsRegistry.Diamond));
            Assert.Equal(3, restored.Inventory[0]?.Count);
            Assert.Single(restored.Inventory, stack => stack is not null);
            if (unload)
                Assert.Null(world.FindEntity(boat.Uuid));
        }
        finally
        {
            inventory.OnRead = null;
            moved.Set();
            await moving.WaitAsync(timeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ContainerPublishedAfterAdmissionDoesNotFailSnapshot(bool unload)
    {
        await using var context = TestServerFactory.CreateServer($"review-published-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var admitted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var published = new ManualResetEventSlim();
        var inventory = new CallbackInventory();
        inventory.OnRead = () =>
        {
            inventory.OnRead = null;
            admitted.TrySetResult();
            Assert.True(published.Wait(timeout), "Public block entity publication blocked behind snapshot locks.");
        };
        chunk.SetBlockEntity(position.X, position.Y, position.Z, new ContainerBlockEntity(inventory)
        {
            Id = "probe:rendezvous",
            BlockPosition = position
        });

        var publishing = Task.Run(async () =>
        {
            await admitted.Task.WaitAsync(timeout);
            var contents = new Container(9);
            contents[0] = new ItemStack(ItemsRegistry.Apple, 5);
            await world.SetBlockEntity(position + Vector.East, new ContainerBlockEntity(contents)
            {
                Id = "probe:published"
            });
            published.Set();
        });
        try
        {
            var region = (Region)world.LoadRegionByChunk(0, 0);
            await Task.Run(() => unload ? region.UnloadChunk(0, 0) : region.FlushAsync()).WaitAsync(timeout);
            var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
            SnapshotInventoryLocking.AssertStack(loaded, position + Vector.East, ItemsRegistry.Apple, 5);
        }
        finally
        {
            inventory.OnRead = null;
            published.Set();
            await publishing.WaitAsync(timeout);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InventoryEditAfterCaptureIsIncludedInTheFinalUnload(bool pluginStorage)
    {
        await using var context = TestServerFactory.CreateServer($"snapshot-revision-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        BaseContainer inventory = pluginStorage ? new OpaqueInventory() : new Container(9);
        inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        chunk.SetBlockEntity(position.X, position.Y, position.Z, new ContainerBlockEntity(inventory)
        {
            Id = "probe:edited",
            BlockPosition = position
        });
        var captured = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var edited = new ManualResetEventSlim();
        var region = (Region)world.LoadRegionByChunk(0, 0);
        region.BeforeInventorySnapshot = () =>
        {
            region.BeforeInventorySnapshot = null;
            captured.TrySetResult();
            Assert.True(edited.Wait(timeout), "The inventory editor did not reach snapshot admission.");
        };
        var editing = Task.Run(async () =>
        {
            await captured.Task.WaitAsync(timeout);
            using (await inventory.LockAsync())
                inventory[0] = new ItemStack(ItemsRegistry.Diamond, 5);

            edited.Set();
        });

        try
        {
            await Task.Run(() => region.UnloadChunk(0, 0)).WaitAsync(timeout);
            var loaded = await SnapshotInventoryLocking.ReloadDiskAsync(region);
            SnapshotInventoryLocking.AssertStack(loaded, position, ItemsRegistry.Diamond, 5);
        }
        finally
        {
            region.BeforeInventorySnapshot = null;
            edited.Set();
            await editing.WaitAsync(timeout);
        }
    }

    [Fact]
    public async Task UnloadSerializesCurrentStructuresWhenAttachmentChangesAfterWritePrediction()
    {
        var folder = Path.Join(Path.GetTempPath(), $"review-structures-{Guid.NewGuid():N}");
        try
        {
            var currentVersion = 1;
            await using var region = new Region(0, 0, folder)
            {
                SaveStructureStarts = (_, _, _) => new NbtCompound("starts")
                {
                    new NbtTag<int>("review-version", currentVersion)
                }
            };
            await region.InitAsync();
            var chunk = new Chunk(0, 0, ChunkGenStage.full);
            chunk.StructureStarts = new NbtCompound("starts")
            {
                new NbtTag<int>("review-version", 1)
            };
            region.SetChunk(chunk);
            currentVersion = 2;
            await region.FlushAsync();
            Assert.False(chunk.IsUnsaved);

            // Attachments are independently synchronized and can change while the snapshot owns tick.
            region.BeforeInventorySnapshot = () =>
            {
                region.BeforeInventorySnapshot = null;
                Obsidian.API.Data.AttachmentStorage.For(chunk).Set("review:data", new NbtTag<int>("", 1));
            };
            await region.UnloadChunk(0, 0);
            var bytes = Assert.IsType<Memory<byte>>(await region.ChunkFile.GetChunkBytesAsync(0, 0));
            using var stream = new MemoryStream(bytes.ToArray());
            var reader = new NbtReader(stream);
            var saved = reader.ReadNextTag() as NbtCompound;
            Assert.NotNull(saved);
            var structures = Assert.IsType<NbtCompound>(saved["structures"]);
            var starts = Assert.IsType<NbtCompound>(structures["starts"]);
            Assert.Equal(2, starts.GetInt("review-version"));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private sealed class CounterEntity() : ContainerBlockEntity(new Container(9))
    {
        internal int Counter { get; set; }

        protected internal override void Save(NbtCompound tag) => tag.Add(new NbtTag<int>("counter", this.Counter));
    }

    private sealed class AuxiliaryContainer() : ContainerBlockEntity(new Container(9))
    {
        internal void Capture() => base.Save(new NbtCompound());
    }

    // A plugin may implement its slots and public revision without writing BaseContainer's host storage.
    private sealed class OpaqueInventory() : BaseContainer(9)
    {
        private readonly ItemStack?[] slots = new ItemStack?[9];
        private long revision;

        public override ItemStack? GetItem(int slot) => this.slots[slot];

        public override void SetItem(int slot, ItemStack? item)
        {
            this.slots[slot] = item;
            this.revision++;
        }

        public override long Revision => this.revision;
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
