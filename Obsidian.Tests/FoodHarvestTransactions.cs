using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Harvest transactions must not duplicate resources, lose bottles or wait on an inventory under the tick.
/// </summary>
public sealed class FoodHarvestTransactions
{
    private static readonly Vector target = new(8, 64, 8);

    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private sealed record Harvest(IBlock Block, Item Item, bool WithItem);

    private static Harvest[] Harvests() =>
    [
        new(BlocksRegistry.Beehive.WithProperty("honey_level", 5), ItemsRegistry.GlassBottle, true),
        new(BlocksRegistry.SweetBerryBush.WithProperty("age", 3), ItemsRegistry.Shears, false)
    ];

    [Fact]
    public async Task AHandChangedAfterReadingCannotHarvestOrExchange()
    {
        await using var context = await StartAsync("harvest-stale");
        var player = Player(context, "User");

        foreach (var harvest in Harvests())
        {
            foreach (var change in new[] { "replace", "count", "slot" })
            {
                player.CurrentHeldItemSlot = 0;
                var original = new ItemStack(harvest.Item, 2);
                player.Inventory.SetItem(36, original);
                context.World.BlockUpdates.SetBlock(target, harvest.Block, Quiet);
                var use = BlockUseTransactions.Read(context, player);

                if (change == "replace")
                    player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.Diamond, 2));
                else if (change == "count")
                    original.Count = 1;
                else
                    player.CurrentHeldItemSlot = 1;

                var result = await UseAsync(use, harvest);
                await context.World.RunQueuedEntityActionsAsync();

                Assert.Equal(UseResult.Fail, result);
                Assert.True(context.World.BlockUpdates.GetBlock(target).IsSameState(harvest.Block));
                Assert.Equal(change == "count" ? 1 : 2, original.Count);
                Assert.Empty(context.World.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>());
                Assert.DoesNotContain(player.Inventory, item => item?.Holder.Id == ItemIds.HoneyBottle);
            }
        }
    }

    [Fact]
    public async Task AFullInventoryDropsOnlyTheExchangedBottle()
    {
        await using var context = await StartAsync("harvest-full");
        var player = Player(context, "User");
        var harvest = Harvests()[0];
        foreach (var slot in Enumerable.Range(9, 36))
            player.Inventory.SetItem(slot, new ItemStack(ItemsRegistry.Stone, 64));

        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.GlassBottle, 2));
        context.World.BlockUpdates.SetBlock(target, harvest.Block, Quiet);
        var use = BlockUseTransactions.Read(context, player);

        Assert.Equal(UseResult.Success, await UseAsync(use, harvest));
        Assert.Equal(UseResult.Fail, await UseAsync(use, harvest));
        await context.World.RunQueuedEntityActionsAsync();

        Assert.Equal(1, player.GetHeldItem()!.Count);
        Assert.Equal(ItemIds.GlassBottle, player.GetHeldItem()!.Holder.Id);
        var dropped = Assert.Single(context.World.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>());
        Assert.Equal(ItemIds.HoneyBottle, dropped.Item.Holder.Id);
        Assert.Equal(1, dropped.Item.Count);
        Assert.Equal(35 * 64, player.Inventory.Where(item => item?.Holder.Id == ItemIds.Stone).Sum(item => item!.Count));
    }

    [Fact]
    public async Task TwoPlayersAtOneHiveOrBushReceiveOneHarvest()
    {
        await using var context = await StartAsync("harvest-race");
        var first = Player(context, "First");
        var second = Player(context, "Second");

        foreach (var harvest in Harvests())
        {
            context.World.BlockUpdates.SetBlock(target, harvest.Block, Quiet);
            BlockLoot.SequencesFor(context.World).Get("minecraft:harvest/sweet_berry_bush").SetSeed(1234);
            foreach (var player in new[] { first, second })
                player.Inventory.SetItem(36, new ItemStack(harvest.Item, 1));

            var uses = new[] { first, second }.Select(player => BlockUseTransactions.Read(context, player)).ToArray();
            var results = await Task.WhenAll(uses.Select(use => Task.Run(() => UseAsync(use, harvest))));
            await context.World.RunQueuedEntityActionsAsync();

            Assert.Single(results, result => result == UseResult.Success);
            Assert.Single(results, result => result == UseResult.Fail);
            if (harvest.WithItem)
            {
                var hands = new[] { first.GetHeldItem()!, second.GetHeldItem()! };
                Assert.Equal(1, hands.Where(item => item.Holder.Id == ItemIds.HoneyBottle).Sum(item => item.Count));
                Assert.Equal(1, hands.Where(item => item.Holder.Id == ItemIds.GlassBottle).Sum(item => item.Count));
            }
            else
            {
                // This seeded harvest's two stacks are recorded in berries_age_3_hand. Racing cannot double them.
                var drops = context.World.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>();
                Assert.Equal(2, drops.Where(item => item.Item.Holder.Id == ItemIds.SweetBerries)
                    .Sum(item => item.Item.Count));
            }
        }
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ABusyInventoryIsTriedWhileTheTickLockIsHeld()
    {
        await using var context = await StartAsync("harvest-busy");
        var player = Player(context, "User");

        foreach (var harvest in Harvests())
        {
            player.Inventory.SetItem(36, new ItemStack(harvest.Item, 2));
            context.World.BlockUpdates.SetBlock(target, harvest.Block, Quiet);
            var use = BlockUseTransactions.Read(context, player);
            UseResult result;
            using (player.Inventory.Lock())
            {
                result = await Task.Run(() =>
                {
                    lock (context.World.Fluids.TickLock)
                        return UseAsync(use, harvest).GetAwaiter().GetResult();
                });
            }

            Assert.Equal(UseResult.Fail, result);
            Assert.True(context.World.BlockUpdates.GetBlock(target).IsSameState(harvest.Block));
            Assert.Equal(2, player.GetHeldItem()!.Count);
            Assert.Empty(context.World.GetEntitiesInRange((VectorD)target, 5).OfType<ItemEntity>());
        }
    }

    private static async Task<UseResult> UseAsync(BlockUse use, Harvest harvest)
    {
        var behavior = harvest.WithItem ? BlocksRegistry.GetUseItemOnBehavior(use.Block.RegistryId)
            : BlocksRegistry.GetUseWithoutItemBehavior(use.Block.RegistryId);
        Assert.NotNull(behavior);
        return harvest.WithItem ? await behavior.UseItemOnAsync(use) : await behavior.UseWithoutItemAsync(use);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task TimedReleaseAndHarvestDetachEachStoredBeeOnce()
    {
        await using var context = await StartAsync("hive-release-race");
        var player = Player(context, "User");
        player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.GlassBottle, 2));
        var hive = PrepareHive(context, 5, Occupant(2400, true), Occupant(0, false));

        // Signal the exact boundary between the real detach and spawn stages. The timer cannot publish
        // until the harvest has finished, and never holds the tick lock while waiting for that signal.
        var detached = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var resume = new ManualResetEventSlim();
        var spawner = new MobSpawner(context.World);
        var ticking = Task.Run(() =>
        {
            try
            {
                object released;
                lock (context.World.Fluids.TickLock)
                    released = HiveStage(spawner, "DetachTimedHiveBees", hive);

                detached.SetResult();
                resume.Wait();
                HiveStage(spawner, "SpawnTimedHiveBees", hive, released);
            }
            catch (Exception exception)
            {
                detached.TrySetException(exception);
                throw;
            }
        });

        try
        {
            await detached.Task;
            var use = BlockUseTransactions.Read(context, player);
            Assert.Equal(UseResult.Success, await UseAsync(use, Harvests()[0]));
        }
        finally
        {
            resume.Set();
            await ticking;
        }

        await context.World.RunQueuedEntityActionsAsync();

        Assert.Equal(2, context.World.GetEntitiesInRange((VectorD)target, 5).OfType<Bee>().Count());
        Assert.Empty(hive.SnapshotTag<NbtList>("bees")!);
        var saved = new NbtCompound();
        hive.Save(saved);
        Assert.True(saved.TryGetTag<NbtList>("bees", out var occupants));
        Assert.Empty(occupants);
        Assert.Equal(1, player.Inventory.Where(item => item?.Holder.Id == ItemIds.HoneyBottle)
            .Sum(item => item!.Count));
    }

    [Fact]
    public async Task QueuedNectarCannotRefillAHarvestedOrReplacedHive()
    {
        await using var context = await StartAsync("hive-queued-honey");
        var player = Player(context, "User");

        foreach (var change in new[] { "unchanged", "harvested", "replaced" })
        {
            var initialHoney = change == "harvested" ? 5 : 4;
            var hive = PrepareHive(context, initialHoney, Occupant(2400, true));
            player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.GlassBottle, 1));
            TickHive(context.World, hive);

            if (change == "harvested")
            {
                var use = BlockUseTransactions.Read(context, player);
                Assert.Equal(UseResult.Success, await UseAsync(use, Harvests()[0]));
                Assert.Equal("0", context.World.BlockUpdates.GetBlock(target).GetProperty("honey_level"));
            }
            else if (change == "replaced")
            {
                context.World.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
                var replacement = BlocksRegistry.Beehive.WithProperty("honey_level", 4);
                context.World.BlockUpdates.SetBlock(target, replacement, Quiet);
            }

            await context.World.RunQueuedEntityActionsAsync();

            var expectedHoney = change switch
            {
                "unchanged" => "5",
                "replaced" => "4",
                _ => "0"
            };
            Assert.Equal(expectedHoney, context.World.BlockUpdates.GetBlock(target).GetProperty("honey_level"));
            if (change == "harvested")
            {
                Assert.Equal(ItemIds.HoneyBottle, player.GetHeldItem()!.Holder.Id);
                Assert.Equal(1, player.GetHeldItem()!.Count);
                player.Inventory.SetItem(36, new ItemStack(ItemsRegistry.GlassBottle, 1));
                var secondUse = BlockUseTransactions.Read(context, player);
                Assert.False((await UseAsync(secondUse, Harvests()[0])).ConsumesAction());
            }
        }
    }

    [Fact]
    public async Task FailedTimedAdmissionDoesNotLoseOtherStoredBees()
    {
        await using var context = await StartAsync("hive-release-rejected");
        var existing = new Bee
        {
            Level = context.World,
            EntityId = Server.GetNextEntityId(),
            Position = (VectorD)target + new VectorD(0.5, 0.2, -0.5)
        };
        context.World.SpawnEntity(existing);
        var unique = new Bee { Level = context.World, HasNectar = true };
        var blocked = Occupant(2400, true);
        var admitted = Occupant(2400, true);
        Assert.True(blocked.TryGetTag<NbtCompound>("entity_data", out var blockedData));
        Assert.True(admitted.TryGetTag<NbtCompound>("entity_data", out var admittedData));
        blockedData.Set(new NbtArray<int>("UUID", EntityNbt.UuidToInts(existing.Uuid)));
        admittedData.Set(new NbtArray<int>("UUID", EntityNbt.UuidToInts(unique.Uuid)));
        var hive = PrepareHive(context, 0, blocked, admitted);

        TickHive(context.World, hive);
        await context.World.RunQueuedEntityActionsAsync();

        var live = context.World.GetEntitiesInRange((VectorD)target, 5).OfType<Bee>().ToArray();
        Assert.Equal(2, live.Length);
        Assert.Same(existing, Assert.Single(live, bee => bee.Uuid == existing.Uuid));
        Assert.False(Assert.Single(live, bee => bee.Uuid == unique.Uuid).HasNectar);
        var retained = Assert.IsType<NbtCompound>(Assert.Single(hive.SnapshotTag<NbtList>("bees")!));
        Assert.True(retained.TryGetTag<NbtCompound>("entity_data", out var retainedData));
        var savedBee = Assert.IsType<Bee>(EntityNbt.Load(retainedData, context.World));
        Assert.Equal(existing.Uuid, savedBee.Uuid);
        Assert.True(savedBee.HasNectar);
        var honey = context.World.BlockUpdates.GetBlock(target).GetProperty("honey_level");
        Assert.True(honey is "1" or "2", $"Only the admitted bee may deliver nectar; honey was {honey}.");

        // Retrying the UUID clash neither duplicates the admitted bee nor delivers the rejected bee's nectar.
        TickHive(context.World, hive);
        await context.World.RunQueuedEntityActionsAsync();

        Assert.Equal(2, context.World.GetEntitiesInRange((VectorD)target, 5).OfType<Bee>().Count());
        Assert.Equal(honey, context.World.BlockUpdates.GetBlock(target).GetProperty("honey_level"));
        var saved = new NbtCompound();
        hive.Save(saved);
        Assert.True(saved.TryGetTag<NbtList>("bees", out var occupants));
        Assert.Single(occupants);
    }

    private static object HiveStage(MobSpawner spawner, string name, params object[] arguments)
    {
        var method = typeof(MobSpawner).GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!;
        return method.Invoke(spawner, arguments)!;
    }

    private static void TickHive(World world, DataBlockEntity hive) =>
        HiveStage(new MobSpawner(world), "TickHive", hive);

    private static DataBlockEntity PrepareHive(RealServerContext context, int honey, params NbtCompound[] bees)
    {
        context.World.LevelData.Raining = false;
        context.World.DayTime = 1000;
        var block = BlocksRegistry.Beehive.WithProperty("honey_level", honey);
        context.World.BlockUpdates.SetBlock(target, block, Quiet);
        var hive = Assert.IsType<DataBlockEntity>(context.World.GetResidentBlockEntity(target));
        var occupants = new NbtList(NbtTagType.Compound, "bees");
        foreach (var bee in bees)
            occupants.Add(bee);

        hive.Set(occupants);
        return hive;
    }

    private static NbtCompound Occupant(int ticks, bool nectar) => new()
    {
        new NbtCompound("entity_data")
        {
            new NbtTag<string>("id", "minecraft:bee"),
            new NbtTag<byte>("HasNectar", nectar ? (byte)1 : (byte)0)
        },
        new NbtTag<int>("ticks_in_hive", ticks),
        new NbtTag<int>("min_ticks_in_hive", 600)
    };

    private static async Task<RealServerContext> StartAsync(string name)
    {
        var context = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        return context;
    }

    private static ServerPlayer Player(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name,
            new VectorD(8.5, 64, 6.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        return player;
    }
}
