using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using ServerPlayer = Obsidian.Entities.Player;

namespace Obsidian.Tests;

/// <summary>
/// Obsidian-only risks of using cauldrons: the hand and the cauldron change in one commit, so a use whose hand, slot or
/// cauldron changed after it was read, whose inventory is busy or whose player died neither loses nor duplicates an
/// item or a level of the cauldron; two players using one cauldron take what it holds once; and a busy inventory is
/// waited for only outside the level's locks. What the uses do is captured from vanilla (cauldrons.json).
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class CauldronExchanges
{
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly Vector target = new(8, 64, 8);

    /// <summary>One use of a cauldron: the cauldron it starts from and what the user holds.</summary>
    /// <param name="Name">The case's name in failures.</param>
    /// <param name="Cauldron">The cauldron's state.</param>
    /// <param name="Held">What the user holds.</param>
    /// <param name="FullInventory">Whether every other storage slot holds stone, so a result drops.</param>
    private sealed record Exchange(string Name, string Cauldron, Func<ItemStack> Held, bool FullInventory = false);

    private static readonly Exchange[] exchanges =
    [
        new("bucket from water", "minecraft:water_cauldron[level=3]", () => Stack("minecraft:bucket", 1)),
        new("stacked buckets from lava, inventory full", "minecraft:lava_cauldron",
            () => Stack("minecraft:bucket", 2), FullInventory: true),
        new("water bucket into empty", "minecraft:cauldron", () => Stack("minecraft:water_bucket", 1)),
        new("stacked glass bottles", "minecraft:water_cauldron[level=1]", () => Stack("minecraft:glass_bottle", 4)),
        new("water bottle", "minecraft:water_cauldron[level=1]",
            () => Stack("minecraft:potion", 1, "{\"minecraft:potion_contents\":{potion:\"minecraft:water\"}}")),
        new("stacked banners, inventory full", "minecraft:water_cauldron[level=1]",
            () => Stack("minecraft:red_banner", 3,
                "{\"minecraft:banner_patterns\":[{pattern:\"minecraft:creeper\",color:\"black\"}]}"),
            FullInventory: true),
        new("dyed shulker box", "minecraft:water_cauldron[level=1]", () => Stack("minecraft:red_shulker_box", 1)),
        new("dyed helmet", "minecraft:water_cauldron[level=1]",
            () => Stack("minecraft:leather_helmet", 1, "{\"minecraft:dyed_color\":255}"))
    ];

    [Fact]
    public async Task InterruptedUsesNeitherLoseNorDuplicateItems()
    {
        await using var context = await StartAsync();
        var world = context.World;
        var player = Player(context, "User");
        var failures = new List<string>();

        foreach (var exchange in exchanges)
        {
            foreach (var interference in new[] { "hand", "slot", "block", "busy", "death" })
            {
                // Read the click, then change what it read before it commits.
                await PrepareAsync(world, player, exchange);
                var use = Read(context, player);
                Interfere(world, player, interference);
                var before = await SettledCensusAsync(world, player);

                IDisposable? held = null;
                if (interference == "busy")
                    held = player.Inventory.Lock();
                else if (interference == "death")
                    player.Health = 0;

                UseResult result;
                try
                {
                    result = await Behavior(use).UseItemOnAsync(use);
                }
                finally
                {
                    held?.Dispose();
                    player.Health = 20;
                    player.CurrentHeldItemSlot = 0;
                }

                var after = await SettledCensusAsync(world, player);
                if (result.ConsumesAction())
                    failures.Add($"{exchange.Name} after {interference}: {result}, not refused");

                if (!Same(before, after))
                    failures.Add($"{exchange.Name} after {interference}: {Describe(before)} became {Describe(after)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task TwoPlayersTakeWhatACauldronHoldsOnce()
    {
        await using var context = await StartAsync();
        var world = context.World;
        var alice = Player(context, "Alice");
        var bob = Player(context, "Bob");
        var failures = new List<string>();

        foreach (var exchange in exchanges)
        {
            // Both players read the same cauldron before either commits; the first use changes it, so the second,
            // which read it as it was, is refused and keeps what Bob holds.
            await PrepareAsync(world, alice, exchange);
            PrepareInventory(bob, exchange);
            var first = Read(context, alice);
            var second = Read(context, bob);
            var bobBefore = Census(world, bob, includeWorld: false);

            var firstResult = await Behavior(first).UseItemOnAsync(first);
            var secondResult = await Behavior(second).UseItemOnAsync(second);
            var bobAfter = await SettledCensusAsync(world, bob, includeWorld: false);

            if (firstResult != UseResult.Success || secondResult.ConsumesAction())
                failures.Add($"{exchange.Name}: {firstResult} then {secondResult}");

            if (!Same(bobBefore, bobAfter))
                failures.Add($"{exchange.Name}: Bob's {Describe(bobBefore)} became {Describe(bobAfter)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task RacingBottlesFillFromTheLastLevelOnce()
    {
        await using var context = await StartAsync();
        var world = context.World;
        var players = new[] { Player(context, "Alice"), Player(context, "Bob") };
        var bottles = exchanges.Single(exchange => exchange.Name == "stacked glass bottles");

        for (var round = 0; round < 20; round++)
        {
            await PrepareAsync(world, players[0], bottles);
            PrepareInventory(players[1], bottles);
            var uses = players.Select(player => Read(context, player)).ToArray();

            // Both uses run at once on the cauldron's last level: one fills a bottle and empties it, the other is
            // refused.
            var racing = uses.Select(use => Task.Run(async () => await Behavior(use).UseItemOnAsync(use)));
            var results = await Task.WhenAll(racing).WaitAsync(TimeSpan.FromSeconds(10));

            Assert.Single(results, result => result == UseResult.Success);
            Assert.Equal(BlocksRegistry.Cauldron.GetHashCode(), world.BlockUpdates.GetBlock(target).GetHashCode());
            var filled = players.Sum(player => Count(player, ItemsRegistry.Potion));
            var empty = players.Sum(player => Count(player, ItemsRegistry.GlassBottle));
            Assert.Equal(1, filled);
            Assert.Equal(7, empty);
        }
    }

    [Fact]
    public async Task ABusyInventoryIsWaitedForOutsideTheLevelLocks()
    {
        await using var context = await StartAsync();
        var world = context.World;
        var player = Player(context, "User");
        var exchange = exchanges.Single(exchange => exchange.Name == "bucket from water");
        await PrepareAsync(world, player, exchange);

        // The click arrives while something else holds the player's inventory.
        var held = player.Inventory.Lock();
        var packet = BlockUpdates.UseOn(target, BlockFace.Up, new VectorF(0.5f, 0.5f, 0.5f));
        var pending = packet.UseAsync(context.Server, player).AsTask();
        await Task.Delay(100);

        // It waits without holding the level's tick lock, so the level goes on meanwhile.
        Assert.False(pending.IsCompleted);
        Assert.True(world.Fluids.TickLock.TryEnter());
        world.Fluids.TickLock.Exit();

        held.Dispose();
        var result = await pending.WaitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(UseResult.Success, result);
        Assert.Equal(BlocksRegistry.Cauldron.GetHashCode(), world.BlockUpdates.GetBlock(target).GetHashCode());
        Assert.Equal(ItemsRegistry.WaterBucket.Id, player.GetHeldItem()?.Holder.Id);
        Assert.Equal(1, player.GetHeldItem()?.Count);
    }

    private static async Task<RealServerContext> StartAsync()
    {
        var context = TestServerFactory.CreateServer($"cauldron-exchanges-{Guid.NewGuid():N}");
        WorldBlockEntities.AddEmptyChunk(context.World);
        await context.World.GetBlockAsync(target);
        return context;
    }

    private static ServerPlayer Player(RealServerContext context, string name)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, name,
            new VectorD(8.5, 64, 5.5));
        context.World.TryAddPlayer(player);
        player.GameMode = GameMode.Survival;
        return player;
    }

    // The exchange's cauldron in a cleared spot, and the user's inventory as the exchange has it.
    private static async Task PrepareAsync(WorldData.World world, ServerPlayer player, Exchange exchange)
    {
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
        await world.RunQueuedEntityActionsAsync();
        foreach (var entity in world.GetEntitiesInRange((VectorD)target, 8).OfType<ItemEntity>().ToArray())
            await world.DestroyEntityAsync(entity);

        world.BlockUpdates.SetBlock(target, VanillaSulfurBlocks.ParseState(exchange.Cauldron), Quiet);
        PrepareInventory(player, exchange);
    }

    // Nothing but the held stack, or stone in every other storage slot too when the exchange's inventory is full.
    private static void PrepareInventory(ServerPlayer player, Exchange exchange)
    {
        player.CurrentHeldItemSlot = 0;
        for (var slot = 0; slot < player.Inventory.Size; slot++)
        {
            var storage = slot >= 9 && slot <= 45;
            var filler = exchange.FullInventory && storage ? new ItemStack(ItemsRegistry.Stone, 64) : null;
            player.Inventory.SetItem(slot, filler);
        }

        player.Inventory.SetItem(player.CurrentHeldItemSlot, exchange.Held());
    }

    // Changes what a use read after it read it: the hand's stack, the selected hotbar slot, or the cauldron.
    private static void Interfere(WorldData.World world, ServerPlayer player, string interference)
    {
        switch (interference)
        {
            case "hand":
                player.Inventory.SetItem(player.CurrentHeldItemSlot, Stack("minecraft:stone", 2));
                break;
            case "slot":
                player.CurrentHeldItemSlot = 1;
                break;
            case "block":
                var other = VanillaSulfurBlocks.ParseState("minecraft:water_cauldron[level=2]");
                world.BlockUpdates.SetBlock(target, other, Quiet);
                break;
        }
    }

    // A stack of the item, with the components of an SNBT patch.
    private static ItemStack Stack(string id, int count, string? components = null)
    {
        if (components is null)
            return new ItemStack(ItemsRegistry.Get(id), count);

        var saved = SnbtReader.ReadCompound($"{{id:\"{id}\",count:{count},components:{components}}}");
        return saved.ItemFromNbt()!;
    }

    private static IBlockUseBehavior Behavior(BlockUse use) =>
        BlocksRegistry.GetUseItemOnBehavior(use.Block.RegistryId)!;

    private static BlockUse Read(RealServerContext context, ServerPlayer player) => new()
    {
        Server = context.Server,
        Level = context.World,
        Player = player,
        Position = target,
        Block = context.World.BlockUpdates.GetBlock(target),
        BlockEntity = context.World.GetResidentBlockEntity(target),
        Face = BlockFace.Up,
        Cursor = new VectorD(0.5, 0.5, 0.5),
        Hand = InteractionHand.MainHand,
        HandSlot = player.CurrentHeldItemSlot,
        Item = player.GetHeldItem(),
        UsedItem = player.GetHeldItem() is ItemStack item ? new ItemStack(item, item.Count) : null,
        Sequence = 1
    };

    private static int Count(ServerPlayer player, Item item) => Enumerable.Range(0, player.Inventory.Size)
        .Select(player.Inventory.GetItem)
        .Where(stack => stack?.Holder.Id == item.Id)
        .Sum(stack => stack!.Count);

    private static async Task<Dictionary<string, int>> SettledCensusAsync(WorldData.World world,
        ServerPlayer player, bool includeWorld = true)
    {
        await world.RunQueuedEntityActionsAsync();
        return Census(world, player, includeWorld);
    }

    // Every stack there is, by item and components: in the player's inventory and, with the world, lying around as
    // item entities, and the cauldron's state, which holds what a use takes or gives.
    private static Dictionary<string, int> Census(WorldData.World world, ServerPlayer player, bool includeWorld = true)
    {
        var counts = new Dictionary<string, int>();
        void Count(ItemStack? stack)
        {
            if (stack is not { Count: > 0 })
                return;

            var key = Key(stack);
            counts[key] = counts.GetValueOrDefault(key) + stack.Count;
        }

        for (var slot = 0; slot < player.Inventory.Size; slot++)
            Count(player.Inventory.GetItem(slot));

        if (!includeWorld)
            return counts;

        foreach (var item in world.GetEntitiesInRange((VectorD)target, 8).OfType<ItemEntity>())
            Count(item.Item);

        counts["block state " + world.BlockUpdates.GetBlock(target).GetHashCode()] = 1;
        return counts;
    }

    // A stack's item and the components it sets, as SNBT.
    private static string Key(ItemStack stack)
    {
        if (!stack.ToNbt().TryGetTag<NbtCompound>("components", out var components))
            return stack.Holder.UnlocalizedName;

        return $"{stack.Holder.UnlocalizedName} {Snbt.Write(components)}";
    }

    private static bool Same(Dictionary<string, int> before, Dictionary<string, int> after) =>
        before.Count == after.Count && before.All(entry => after.GetValueOrDefault(entry.Key) == entry.Value);

    private static string Describe(Dictionary<string, int> census) =>
        string.Join(", ", census.OrderBy(entry => entry.Key).Select(entry => $"{entry.Key} x{entry.Value}"));
}
