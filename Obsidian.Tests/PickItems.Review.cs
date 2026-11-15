#nullable enable
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.Nbt;
using Obsidian.WorldData;
using Obsidian.API.BlockEntities;
using System.Collections.Immutable;
using Obsidian.Tests.Fakes;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed partial class PickItems
{
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickCreativeReachAppliesAdditionBeforeMultiplicationAndClamping()
    {
        await using var context = TestServerFactory.CreateServer($"pick-reach-modifiers-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Stone);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        Assert.True(player.SetAttributeModifier("minecraft:entity_interaction_range", "test:double", 1,
            AttributeOperation.MulTotal));
        Assert.True(player.SetAttributeModifier("minecraft:block_interaction_range", "test:double", 1,
            AttributeOperation.MulBase));
        var stand = new ArmorStand { Level = context.World, EntityId = Server.GetNextEntityId(), Position = Start };
        Assert.True(context.World.TryAddEntity(stand));
        var client = (TestClient)player.Client;
        client.ClearPackets();

        player.Position = new VectorD(stand.CurrentBox.Max.X + 13, 64, 8.5);
        await PickEntityAsync(context.Server, player, stand.EntityId);
        Assert.Empty(Delivered(client));
        player.Position -= new VectorD(0.01, 0, 0);
        await PickEntityAsync(context.Server, player, stand.EntityId);
        Assert.Contains(Delivered(client), packet => packet is ContainerSetSlotPacket);

        client.ClearPackets();
        player.Position = new VectorD(20, 64, 8.5);
        await PickBlockAsync(context.Server, player, Target);
        Assert.Empty(Delivered(client));
        player.Position -= new VectorD(0.01, 0, 0);
        await PickBlockAsync(context.Server, player, Target);
        Assert.Contains(Delivered(client), packet => packet is ContainerSetSlotPacket);

        Assert.True(player.TryUpdateAttribute("minecraft:block_interaction_range", 64));
        Assert.Equal(64, PlayerBase.BlockInteractionRange(player));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickMatchesEffectiveDefaultsButHonorsComponentRemovals()
    {
        await using var context = TestServerFactory.CreateServer($"pick-defaults-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Chest);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var existing = new ItemStack(ItemsRegistry.Chest, 19);
        player.Inventory.SetItem(39, existing);
        var client = (TestClient)player.Client;
        client.ClearPackets();

        await PickBlockAsync(context.Server, player, Target, true);

        Assert.Equal(3, Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client))).Slot);
        Assert.Same(existing, player.GetHeldItem());
        Assert.Null(player.Inventory.GetItem(36));

        existing.Remove(DataComponentType.Container);
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target, true);
        Assert.Contains(Delivered(client), packet => packet is ContainerSetSlotPacket);
        Assert.NotSame(existing, player.GetHeldItem());
        Assert.Same(existing, player.Inventory.GetItem(39));
        Assert.Equal(19, existing.Count);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickLecternDoesNotCollectContainerComponents()
    {
        await using var context = TestServerFactory.CreateServer($"pick-lectern-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Lectern);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var client = (TestClient)player.Client;
        client.ClearPackets();

        await PickBlockAsync(context.Server, player, Target, true);

        Assert.Contains(Delivered(client), packet => packet is ContainerSetSlotPacket);
        Assert.True(player.GetHeldItem()!.Is(ItemsRegistry.Lectern));
        Assert.False(player.GetHeldItem()!.ContainsKey(DataComponentType.Container));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickBlockSnapshotCapturesHiveStateAndOccupantsTogether()
    {
        await using var context = TestServerFactory.CreateServer($"pick-hive-snapshot-{Guid.NewGuid():N}");
        var chunk = LoadChunk(context.World);
        var fullHive = BlocksRegistry.Beehive.WithProperty("honey_level", 5);
        await context.World.SetBlockAsync(Target, fullHive);
        var hive = Assert.IsType<DataBlockEntity>(await context.World.GetBlockEntityAsync(Target));
        hive.Set(new NbtList(NbtTagType.Compound, "bees"));
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var client = (TestClient)player.Client;
        client.ClearPackets();
        Task pending;

        // Stop Save at the data read. A harvest may proceed only if the picker released the tick lock too soon.
        lock (hive.Data)
        {
            pending = StartPickThread(() => PickBlockAsync(context.Server, player, Target, true));
            Assert.False(pending.IsCompleted);
            if (context.World.Fluids.TickLock.TryEnter())
            {
                try
                {
                    chunk.SetBlock(Target.X, Target.Y, Target.Z, fullHive.WithProperty("honey_level", 0));
                    hive.Set(new NbtList(NbtTagType.Compound, "bees")
                    {
                        new NbtCompound
                        {
                            new NbtCompound("entity_data") { new NbtTag<string>("id", "minecraft:bee") },
                            new NbtTag<int>("ticks_in_hive", 0),
                            new NbtTag<int>("min_ticks_in_hive", 600)
                        }
                    });
                }
                finally
                {
                    context.World.Fluids.TickLock.Exit();
                }
            }
        }

        await pending;
        Assert.Contains(Delivered(client), packet => packet is ContainerSetSlotPacket);
        var picked = player.GetHeldItem()!;
        var state = picked.GetComponent<SimpleDataComponent<ImmutableArray<BlockStateProperty>>>(
            DataComponentType.BlockState)!;
        Assert.Equal("5", Assert.Single(state.Value).Value);
        Assert.Empty(picked.GetComponent<BeesDataComponent>(DataComponentType.Bees)!.Occupants);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickEntityRejectsRemovedButStillRegisteredArmorStand()
    {
        await using var context = TestServerFactory.CreateServer($"pick-entity-snapshot-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var stand = new ArmorStand { Level = context.World, EntityId = Server.GetNextEntityId(), Position = Start };
        Assert.True(context.World.TryAddEntity(stand));
        var client = (TestClient)player.Client;
        client.ClearPackets();
        Task removal;
        ValueTask pick;

        using (context.World.TransferLock.Enter())
        {
            // RemoveAsync sets IsRemoved before entering the transfer lock. Keep eviction blocked deliberately.
            removal = StartPickThread(() => stand.RemoveAsync());
            Assert.True(stand.IsRemoved);
            Assert.True(stand.IsMemberOf(context.World));
            pick = PickEntityAsync(context.Server, player, stand.EntityId);
            Assert.True(pick.IsCompletedSuccessfully);
        }

        await pick;
        await removal;
        Assert.Empty(Delivered(client));
        Assert.Null(player.GetHeldItem());

        var replacement = new ArmorStand
        {
            Level = context.World,
            EntityId = Server.GetNextEntityId(),
            Position = Start
        };
        Assert.True(context.World.TryAddEntity(replacement));
        await PickEntityAsync(context.Server, player, replacement.EntityId);
        Assert.Contains(Delivered(client), packet => packet is ContainerSetSlotPacket);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickPreservesDecodedNestedPatchesWhenMatchingVanillaTemplates()
    {
        await using var context = TestServerFactory.CreateServer($"pick-nested-patches-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        await context.World.SetBlockAsync(Target, BlocksRegistry.Chest);
        var chest = Assert.IsAssignableFrom<ContainerBlockEntity>(await context.World.GetBlockEntityAsync(Target));
        var requestedTemplate = new ItemStack(ItemsRegistry.Chest, 3,
            ComponentBuilder.Container with { Value = [] });
        var requestedInner = requestedTemplate.ToNbt().ItemFromNbt()!;
        chest.Inventory.SetItem(0, requestedInner);
        var existing = new ItemStack(ItemsRegistry.Chest, 7,
            ComponentBuilder.Container with { Value = [new ItemStack(ItemsRegistry.Chest, 3)] });
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        player.Inventory.SetItem(39, existing);
        var client = (TestClient)player.Client;
        client.ClearPackets();

        // Vanilla ItemStackTemplate equality retains the decoded inner item's explicit component patch.
        await PickBlockAsync(context.Server, player, Target, true);

        Assert.Collection(Delivered(client),
            packet => Assert.Equal(0, Assert.IsType<SetHeldSlotPacket>(packet).Slot),
            packet => AssertSlot(packet, 36, ItemsRegistry.Chest, 1, 1));
        var picked = player.GetHeldItem()!;
        Assert.NotSame(existing, picked);
        Assert.Same(existing, player.Inventory.GetItem(39));
        Assert.Equal(7, existing.Count);
        var pickedInner = picked.GetComponent<SimpleDataComponent<ImmutableArray<ItemStack>>>(
            DataComponentType.Container)!.Value[0];
        Assert.Equal(3, pickedInner.Count);
        Assert.True(pickedInner.ContainsKey(DataComponentType.Container));

        // Repeating the pick selects the matching template without creating another item.
        player.CurrentHeldItemSlot = 3;
        client.ClearPackets();
        await PickBlockAsync(context.Server, player, Target, true);
        Assert.Equal(0, Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client))).Slot);
        Assert.Same(picked, player.GetHeldItem());
        Assert.Equal(1, picked.Count);
        Assert.Same(existing, player.Inventory.GetItem(39));
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PickEntityLookupDoesNotAllocateForUnrelatedEntities()
    {
        await using var context = TestServerFactory.CreateServer($"pick-lookup-{Guid.NewGuid():N}");
        LoadChunk(context.World);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Picker", Start);
        await player.SetGamemodeAsync(GameMode.Creative);
        var stand = new ArmorStand { Level = context.World, EntityId = Server.GetNextEntityId(), Position = Start };
        Assert.True(context.World.TryAddEntity(stand));
        await PickEntityAsync(context.Server, player, stand.EntityId);
        var baseline = MeasurePick();
        for (var index = 0; index < 4000; index++)
        {
            Assert.True(context.World.TryAddEntity(new ItemEntity
            {
                Level = context.World,
                EntityId = Server.GetNextEntityId(),
                Position = Start,
                Item = new ItemStack(ItemsRegistry.Dirt)
            }));
        }
        var client = (TestClient)player.Client;
        client.ClearPackets();

        var allocated = MeasurePick();

        Assert.IsType<SetHeldSlotPacket>(Assert.Single(Delivered(client)));
        Assert.True(allocated < baseline + 8_000,
            $"Picking allocated {allocated} bytes, versus {baseline} without unrelated entities.");

        long MeasurePick()
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            var pick = PickEntityAsync(context.Server, player, stand.EntityId);
            Assert.True(pick.IsCompletedSuccessfully);
            pick.GetAwaiter().GetResult();
            return GC.GetAllocatedBytesForCurrentThread() - before;
        }
    }

    private static Task StartPickThread(Func<ValueTask> pick)
    {
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                pick().GetAwaiter().GetResult();
                completed.SetResult();
            }
            catch (Exception error)
            {
                completed.SetException(error);
            }
        });
        thread.IsBackground = true;
        thread.Start();
        Assert.True(SpinWait.SpinUntil(() => completed.Task.IsCompleted
            || (thread.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(5)));
        return completed.Task;
    }
}
