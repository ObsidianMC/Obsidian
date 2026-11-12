#nullable enable

using Obsidian.API;
using Obsidian.API.Containers;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.Plugins;
using Obsidian.Registries;
using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

[Collection(nameof(PluginTagFiles))]
public sealed class SnapshotInventoryLocking
{
    private static readonly TimeSpan timeout = TimeSpan.FromSeconds(10);
    private static readonly Vector position = new(8, 70, 8);

    [Fact]
    public async Task VehicleSnapshotWindowAndPickupCannotDeadlockAndSaveTheFinalItems()
    {
        await using var context = TestServerFactory.CreateServer($"vehicle-snapshot-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        var cart = new ChestMinecart
        {
            Level = world,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)position
        };
        cart.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);
        // Assign the vehicle's rank before the player's, just as opening a vehicle before another hand operation can.
        using (await cart.Inventory.LockAsync())
        {
        }

        Assert.True(world.TrySpawnEntity(cart, hostOwned: true));
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "User", (VectorD)position);
        var hand = await player.Inventory.LockAsync();
        var snapshotHeld = Signal();
        var pickupRequested = Signal();
        using var resumeSnapshot = new ManualResetEventSlim();
        region.BeforeInventorySnapshot = () =>
        {
            region.BeforeInventorySnapshot = null;
            snapshotHeld.TrySetResult();
            Assert.True(resumeSnapshot.Wait(timeout), "The window and pickup did not reach the snapshot rendezvous.");
        };

        Task save = Task.Run(() => region.FlushAsync());
        Task window = Task.CompletedTask;
        Task pickup = Task.CompletedTask;
        try
        {
            await snapshotHeld.Task.WaitAsync(timeout);
            var lockingWindow = BaseContainer.LockWindowAsync(cart.Inventory, player.Inventory).AsTask();
            Assert.False(lockingWindow.IsCompleted);
            var vehicleBusy = !cart.Inventory.TryLock(out var unexpected);
            unexpected?.Dispose();
            Assert.True(vehicleBusy);
            window = FinishWindowAsync();
            pickup = Task.Run(() =>
            {
                // The pickup edge: its hand is owned until it has entered and left the transfer lock.
                pickupRequested.TrySetResult();
                using (world.TransferLock.Enter())
                {
                }

                hand.Dispose();
            });
            await pickupRequested.Task.WaitAsync(timeout);
            resumeSnapshot.Set();

            try
            {
                await Task.WhenAll(save, window, pickup).WaitAsync(timeout);
            }
            catch (TimeoutException)
            {
                Assert.Fail("Vehicle snapshot, window and pickup did not finish: " +
                    $"save={save.IsCompleted}, window={window.IsCompleted}, pickup={pickup.IsCompleted}. " +
                    "A snapshot must not wait for vehicle inventory while holding transfer.");
            }

            async Task FinishWindowAsync()
            {
                using (await lockingWindow)
                    cart.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 4);
            }
        }
        finally
        {
            hand.Dispose();
            resumeSnapshot.Set();
            region.BeforeInventorySnapshot = null;
            await Task.WhenAll(save, window, pickup).WaitAsync(timeout);
        }

        // Read a new region from disk, before disposal could perform an extra save that hides stale data.
        var loaded = await ReloadDiskAsync(region);
        var savedCart = Assert.Single(loaded.PendingEntities);
        var restored = Assert.IsType<ChestMinecart>(EntityNbt.Load(EntityNbt.ToNbt(savedCart), world));
        Assert.Equal(4, restored.Inventory[0]?.Count);
        Assert.True(restored.Inventory[0]?.Is(ItemsRegistry.Diamond));
        Assert.Single(restored.Inventory, item => item is not null);
    }

    [Fact]
    public async Task BusyContainerSnapshotsAndUnloadsSaveEveryFinalStackWithoutPartialRetirement()
    {
        foreach (var unload in new[] { false, true })
        {
            await using var context = TestServerFactory.CreateServer($"container-snapshot-{Guid.NewGuid():N}");
            var world = context.World;
            WorldBlockEntities.AddEmptyChunk(world);
            await world.SetBlockAsync(position, BlocksRegistry.Chest);
            await world.SetBlockAsync(position + Vector.East, BlocksRegistry.Chest);
            var first = Assert.IsAssignableFrom<ContainerBlockEntity>(world.GetResidentBlockEntity(position));
            // Also exercise the host overlay for a plugin-style container, whose Save override is not called
            // by chunk serialization. An unknown type reloads as retained data with its host inventory intact.
            var second = new ContainerBlockEntity(new Container(9))
            {
                Id = "probe:snapshot",
                BlockPosition = position + Vector.East
            };
            var resident = (Chunk)world.GetLoadedChunk(0, 0)!;
            resident.SetBlockEntity(second.BlockPosition.X, second.BlockPosition.Y, second.BlockPosition.Z, second);
            first.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 1);
            second.Inventory[0] = new ItemStack(ItemsRegistry.Apple, 2);
            var region = (Region)world.LoadRegionByChunk(0, 0);
            await region.FlushAsync();
            var busy = Signal();
            region.InventorySnapshotDeferred = () => busy.TrySetResult();
            var held = await second.Inventory.LockAsync();
            Task saving = Task.CompletedTask;
            Task guarded = Task.CompletedTask;
            try
            {
                // A save cannot unwind a caller's outer tick lock. Refuse busy admission instead of waiting there.
                guarded = Task.Run(() =>
                {
                    lock (world.Fluids.TickLock)
                    {
                        Assert.Throws<InvalidOperationException>(() => region.FlushAsync().GetAwaiter().GetResult());
                    }
                });
                await guarded.WaitAsync(timeout);
                saving = Task.Run(() => unload ? region.UnloadChunk(0, 0) : region.FlushAsync());
                await busy.Task.WaitAsync(timeout);
                Assert.False(first.IsRemoved);
                Assert.False(second.IsRemoved);
                Assert.False(first.Inventory.IsRetired);
                Assert.False(second.Inventory.IsRetired);

                // A menu owning inventory can still write the world while the save waits outside its locks.
                await Task.Run(() => world.SetBlockAsync(position + Vector.North, BlocksRegistry.Stone).AsTask())
                    .WaitAsync(timeout);
                using (await first.Inventory.LockAsync())
                    first.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 3);

                second.Inventory[0] = new ItemStack(ItemsRegistry.Apple, 4);
            }
            finally
            {
                held.Dispose();
                await guarded.WaitAsync(timeout);
                await saving.WaitAsync(timeout);
                region.InventorySnapshotDeferred = null;
            }

            Assert.Equal(unload, first.Inventory.IsRetired);
            Assert.Equal(unload, second.Inventory.IsRetired);
            var loaded = await ReloadDiskAsync(region);
            AssertStack(loaded, position, ItemsRegistry.Diamond, 3);
            AssertStack(loaded, position + Vector.East, ItemsRegistry.Apple, 4);
            var marker = position + Vector.North;
            Assert.Equal(BlocksRegistry.Stone.RegistryId, loaded.GetBlock(marker.X, marker.Y, marker.Z).RegistryId);
        }
    }

    [Fact]
    public async Task BusyPluginPreservationKeepsContentsThroughUnloadAndDiskReload()
    {
        await using var context = TestServerFactory.CreateServer($"preserve-snapshot-{Guid.NewGuid():N}");
        var world = context.World;
        var chunk = WorldBlockEntities.AddEmptyChunk(world);
        await world.GetBlockAsync(position);
        await using var manager = BlockUpdates.CreatePluginManager(context);
        var archive = Path.Join(Path.GetDirectoryName(world.PlayerDataPath)!, "preserve.obby");
        await File.WriteAllBytesAsync(archive, PluginLoading.ProbeArchive("1.0.0"));
        var plugin = Assert.IsType<PluginContainer>(await manager.LoadPluginAsync(archive));
        Assert.True(BlocksRegistry.TryGetPluginBlock("probe:crate", out var crate));
        await world.SetBlockAsync(position, crate.DefaultState);
        var container = Assert.IsAssignableFrom<ContainerBlockEntity>(world.GetResidentBlockEntity(position));
        container.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 2);
        var held = await container.Inventory.LockAsync();
        try
        {
            // The placement/unloading race calls Keep while already under tick. It must defer the whole save,
            // not carry a new inventory lock across the plugin's final Save override.
            await Task.Run(() =>
            {
                lock (world.Fluids.TickLock)
                    PluginBlockEntities.Keep(world, chunk, container, plugin);
            }).WaitAsync(timeout);
            Assert.Same(container, world.GetResidentBlockEntity(position));
            Assert.False(container.Inventory.IsRetired);
            await world.SetBlockAsync(position + Vector.North, BlocksRegistry.Stone);
            container.Inventory[0] = new ItemStack(ItemsRegistry.Diamond, 5);
        }
        finally
        {
            held.Dispose();
        }

        await world.RunQueuedEntityActionsAsync().WaitAsync(timeout);
        Assert.IsType<DataBlockEntity>(world.GetResidentBlockEntity(position));
        Assert.True(container.Inventory.IsRetired);
        await manager.UnloadPluginAsync(plugin);
        var region = (Region)world.LoadRegionByChunk(0, 0);
        await region.FlushAsync();
        var loaded = await ReloadDiskAsync(region);
        var data = Assert.IsType<DataBlockEntity>(loaded.GetBlockEntity(position.X, position.Y, position.Z));
        var restored = Assert.IsAssignableFrom<ContainerBlockEntity>(data.KeptContainer());
        Assert.Equal(5, restored.Inventory[0]?.Count);
        Assert.True(restored.Inventory[0]?.Is(ItemsRegistry.Diamond));
        Assert.Single(restored.Inventory, item => item is not null);
    }

    [Fact]
    public async Task BusyCampfireProjectionRetriesAndDoesNotAffectItsSavedContents()
    {
        await using var context = TestServerFactory.CreateServer($"campfire-snapshot-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);
        await world.SetBlockAsync(position, BlocksRegistry.Campfire);
        var campfire = Assert.IsType<CampfireBlockEntity>(world.GetResidentBlockEntity(position));
        campfire.Inventory[0] = new ItemStack(ItemsRegistry.Beef, 1);
        campfire.ClientData = campfire.GetUpdateData();
        var original = campfire.ClientData;
        using (await campfire.Inventory.LockAsync())
        {
            campfire.Inventory[0] = new ItemStack(ItemsRegistry.Apple, 1);
            var busy = Task.Run(() => campfire.GetUpdateData());
            Assert.Same(original, await busy.WaitAsync(timeout));
        }

        var projected = campfire.GetUpdateData()!;
        var visible = Assert.IsType<NbtList>(projected["Items"]).OfType<NbtCompound>().Single();
        Assert.Equal("minecraft:apple", visible.GetString("id"));
        var region = (Region)world.LoadRegionByChunk(0, 0);
        await region.FlushAsync();
        var loaded = await ReloadDiskAsync(region);
        AssertStack(loaded, position, ItemsRegistry.Apple, 1);
    }

    internal static void AssertStack(Chunk chunk, Vector at, Item item, int count)
    {
        var entity = chunk.GetBlockEntity(at.X, at.Y, at.Z);
        var container = entity is DataBlockEntity data
            ? Assert.IsAssignableFrom<ContainerBlockEntity>(data.KeptContainer())
            : Assert.IsAssignableFrom<ContainerBlockEntity>(entity);
        Assert.True(container.Inventory[0]?.Is(item));
        Assert.Equal(count, container.Inventory[0]?.Count);
        Assert.Single(container.Inventory, stack => stack is not null);
    }

    // Inspect exactly what the completed operation wrote, before server disposal can save it again.
    // Read committed bytes through the owning file handle, then reload those exact NBT bytes in an isolated region.
    internal static async Task<Chunk> ReloadDiskAsync(Region source, int x = 0, int z = 0)
    {
        var folder = Path.Join(Path.GetTempPath(), $"obsidian-snapshot-copy-{Guid.NewGuid():N}");
        try
        {
            foreach (var (name, sourceFile) in new[] { ("regions", source.ChunkFile), ("entities", source.EntityFile) })
            {
                var destination = Path.Join(folder, name);
                Directory.CreateDirectory(destination);
                if (await sourceFile.GetChunkBytesAsync(x, z) is not Memory<byte> bytes)
                    continue;

                await using var file = new RegionFile(Path.Join(destination, "r.0.0.mca"), NbtCompression.ZLib);
                await file.InitializeAsync();
                var compressed = file.Compress(bytes.Span);
                try
                {
                    await file.SetChunkAsync(x, z, compressed.Written.ToArray());
                }
                finally
                {
                    compressed.Return();
                }
            }

            await using var disk = new Region(0, 0, folder);
            await disk.InitAsync();
            return Assert.IsType<Chunk>(await disk.GetChunkAsync(x, z));
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
