using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.Commands;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net.Packets.Play.Serverbound;
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
/// Obsidian-only risks of the blocks that hold items (flower pots, chiseled bookshelves, decorated pots, lodestone
/// compasses, jukeboxes): an exchange interrupted or raced by another player neither loses nor duplicates an item,
/// and their block entities save in vanilla's form. What the uses do is captured from vanilla (block_use.json).
/// </summary>
[Collection(nameof(PluginTagFiles))]
public sealed class ItemHoldingBlocks
{
    private const BlockUpdateFlags Quiet = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;

    private static readonly Vector target = new(8, 64, 8);

    // A point over the bookshelf's slot 0 on its front (north) face.
    private static readonly VectorD slotZero = new(0.83, 0.75, 0);

    /// <summary>One exchange: the block it starts from, what the user holds, and which use runs.</summary>
    /// <param name="Name">The case's name in failures.</param>
    /// <param name="Setup">Sets the block, and its block entity's items, quietly.</param>
    /// <param name="Held">What the user holds, or null for nothing.</param>
    /// <param name="Use">Runs the use and returns how it went.</param>
    private sealed record Exchange(
        string Name,
        Action<WorldData.World> Setup,
        Func<ItemStack?> Held,
        Func<BlockUse, ValueTask<UseResult>> Use);

    private static readonly Exchange[] exchanges =
    [
        new("bookshelf insert", world => Place(world, "minecraft:chiseled_bookshelf"),
            () => Stack("minecraft:book", 2),
            use => Behavior(use).UseItemOnAsync(use)),
        new("bookshelf take", world => Place(world, "minecraft:chiseled_bookshelf[slot_0_occupied=true]",
            Stack("minecraft:book", 1)),
            () => null,
            use => Behavior(use).UseWithoutItemAsync(use)),
        new("pot insert", world => Place(world, "minecraft:decorated_pot", Stack("minecraft:diamond", 63)),
            () => Stack("minecraft:diamond", 3),
            use => Behavior(use).UseItemOnAsync(use)),
        new("flower pot plant", world => Place(world, "minecraft:flower_pot"),
            () => Stack("minecraft:dandelion", 2),
            use => Behavior(use).UseItemOnAsync(use)),
        new("flower pot take", world => Place(world, "minecraft:potted_poppy"),
            () => null,
            use => Behavior(use).UseWithoutItemAsync(use)),
        new("lodestone compass", world => Place(world, "minecraft:lodestone"),
            () => Stack("minecraft:compass", 2),
            UseItemOnPacket.UseCompassAsync)
    ];

    [Fact]
    public async Task InterruptedExchangesNeitherLoseNorDuplicateItems()
    {
        await using var context = await StartAsync();
        var world = context.World;
        var player = Player(context, "User");
        var failures = new List<string>();

        foreach (var exchange in exchanges)
        {
            foreach (var interference in new[] { "hand", "block", "entity", "busy", "death" })
            {
                // Read the click, then change what it read before it commits.
                await PrepareAsync(world, player, exchange);
                var use = Read(context, player);
                Interfere(world, player, exchange, interference);
                var before = await SettledCensusAsync(world, player);

                IDisposable? held = null;
                if (interference == "busy")
                    held = BlockInventory(world)?.Lock() ?? player.Inventory.Lock();
                else if (interference == "death")
                    player.Health = 0;

                UseResult result;
                try
                {
                    result = await exchange.Use(use);
                }
                finally
                {
                    held?.Dispose();
                    player.Health = 20;
                }

                // The entity case reads a block entity that's gone: blocks without one still commit.
                var refused = !result.ConsumesAction() || interference == "entity" && use.BlockEntity is null;
                var after = await SettledCensusAsync(world, player);
                if (!refused)
                    failures.Add($"{exchange.Name} after {interference}: {result}, not refused");

                if (!Same(before, after))
                    failures.Add($"{exchange.Name} after {interference}: {Describe(before)} became {Describe(after)}");
            }
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public async Task CompetingExchangesMoveEachItemOnce()
    {
        await using var context = await StartAsync();
        var world = context.World;
        var alice = Player(context, "Alice");
        var bob = Player(context, "Bob");
        var failures = new List<string>();

        foreach (var exchange in exchanges)
        {
            // Both players read the same block before either commits; only one exchange fits (the pot holds 63).
            await PrepareAsync(world, alice, exchange);
            bob.Inventory.SetItem(bob.CurrentHeldItemSlot, exchange.Held());
            var first = Read(context, alice);
            var second = Read(context, bob);
            var before = Merge(Census(world, alice), Census(world, bob, includeBlock: false));

            var firstResult = await exchange.Use(first);
            var secondResult = await exchange.Use(second);
            var after = Merge(await SettledCensusAsync(world, alice), Census(world, bob, includeBlock: false));

            var compass = exchange.Name == "lodestone compass";
            if (!firstResult.ConsumesAction() || secondResult.ConsumesAction() && !compass)
                failures.Add($"{exchange.Name}: {firstResult} then {secondResult}");

            if (!Same(before, after))
                failures.Add($"{exchange.Name}: {Describe(before)} became {Describe(after)}");
        }

        Assert.True(failures.Count == 0, string.Join(Environment.NewLine, failures));
    }

    /// <summary>A block entity's saved data, vanilla's, and what loading it must give back.</summary>
    private sealed record Saved(string Name, string State, string Data, Action<IBlockEntity> Check);

    [Fact]
    public async Task NewBlockEntitiesSaveAndLoadInVanillasForm()
    {
        await using var context = await StartAsync();
        var world = context.World;
        Saved[] rows =
        [
            new("bookshelf with books", "minecraft:chiseled_bookshelf[slot_0_occupied=true,slot_4_occupied=true]",
                "{Items:[{Slot:0b,count:1,id:\"minecraft:book\"},{Slot:4b,count:1,id:\"minecraft:enchanted_book\"}],"
                + "components:{},id:\"minecraft:chiseled_bookshelf\",last_interacted_slot:4}",
                entity =>
                {
                    var shelf = Assert.IsType<ChiseledBookshelfBlockEntity>(entity);
                    Assert.Equal(4, shelf.LastInteractedSlot);
                    Assert.Equal(ItemsRegistry.Get("minecraft:enchanted_book").Id, shelf.Inventory[4]!.Holder.Id);
                }),
            new("empty bookshelf", "minecraft:chiseled_bookshelf",
                "{Items:[],components:{},id:\"minecraft:chiseled_bookshelf\",last_interacted_slot:-1}",
                entity => Assert.Equal(0, Assert.IsType<ChiseledBookshelfBlockEntity>(entity).AnalogSignal())),
            new("pot with sherds and items", "minecraft:decorated_pot",
                "{components:{},id:\"minecraft:decorated_pot\",item:{count:9,id:\"minecraft:diamond\"},"
                + "sherds:{back:{id:\"minecraft:angler_pottery_sherd\"},front:{id:\"minecraft:heart_pottery_sherd\"},"
                + "left:{id:\"minecraft:brick\"},right:{id:\"minecraft:brick\"}}}",
                entity => Assert.Equal(9, Assert.IsType<DecoratedPotBlockEntity>(entity).Inventory[0]!.Count)),
            new("pot waiting for loot", "minecraft:decorated_pot",
                "{LootTable:\"minecraft:archaeology/desert_pyramid\",LootTableSeed:5L,components:{},"
                + "id:\"minecraft:decorated_pot\"}",
                entity => Assert.Null(Assert.IsType<DecoratedPotBlockEntity>(entity).Inventory[0]))
        ];

        foreach (var row in rows)
        {
            var vanilla = SnbtReader.ReadCompound(row.Data);
            var id = vanilla.GetString("id")!;
            world.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
            world.BlockUpdates.SetBlock(target, VanillaSulfurBlocks.ParseState(row.State), Quiet);

            // Loaded as a chunk loads it, saved as a chunk saves it, then loaded again from what was saved.
            var loaded = BlockEntityNbt.Load(vanilla, id, target);
            row.Check(loaded);
            var saved = Without(BlockEntityNbt.Save(loaded)!, "x", "y", "z", "keepPacked");
            Assert.Equal(Snbt.Write(vanilla), Snbt.Write(saved));

            var reloaded = BlockEntityNbt.Load(saved, id, target);
            row.Check(reloaded);
        }
    }

    [Fact]
    public void JukeboxesFromOlderObsidianWorldsKeepPlaying()
    {
        // Before vanilla's ticks_since_song_started, Obsidian saved IsPlaying and ObsidianSongElapsed.
        var jukebox = new DataBlockEntity { Id = "minecraft:jukebox", BlockPosition = target };
        jukebox.Set(Stack("minecraft:music_disc_cat", 1).ToNbt("RecordItem"));
        jukebox.Set("IsPlaying", true);
        jukebox.Set("ObsidianSongElapsed", 40);

        Assert.Equal(40, AbstractLevel.SongTicks(jukebox));

        var data = jukebox.SnapshotData();
        Assert.True(data.TryGetNumber<long>("ticks_since_song_started", out var ticks));
        Assert.Equal(40, ticks);
        Assert.False(data.HasTag("IsPlaying"));
        Assert.False(data.HasTag("ObsidianSongElapsed"));

        var stopped = new DataBlockEntity { Id = "minecraft:jukebox", BlockPosition = target };
        stopped.Set("IsPlaying", false);
        stopped.Set("ObsidianSongElapsed", 40);
        Assert.Null(AbstractLevel.SongTicks(stopped));
        Assert.Empty(stopped.SnapshotData());
    }

    private static async Task<RealServerContext> StartAsync()
    {
        var context = TestServerFactory.CreateServer($"item-holding-{Guid.NewGuid():N}");
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

    // The exchange's block in a cleared spot, and the user's inventory empty but for the held stack.
    private static async Task PrepareAsync(WorldData.World world, ServerPlayer player, Exchange exchange)
    {
        // Clearing the last case's block drops what it held on a queued action, which goes too.
        world.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
        await world.RunQueuedEntityActionsAsync();
        foreach (var entity in world.GetEntitiesInRange((VectorD)target, 6).OfType<ItemEntity>().ToArray())
            await world.DestroyEntityAsync(entity);

        exchange.Setup(world);

        for (var slot = 0; slot < player.Inventory.Size; slot++)
            player.Inventory.SetItem(slot, null);

        player.Inventory.SetItem(player.CurrentHeldItemSlot, exchange.Held());
    }

    // Changes what a use read after it read it: the hand, the block, or the block entity (the same block again).
    private static void Interfere(WorldData.World world, ServerPlayer player, Exchange exchange, string interference)
    {
        switch (interference)
        {
            case "hand":
                player.Inventory.SetItem(player.CurrentHeldItemSlot, Stack("minecraft:stone", 2));
                break;
            case "block":
                world.BlockUpdates.SetBlock(target, BlocksRegistry.Get("minecraft:stone"), Quiet);
                break;
            case "entity":
                world.BlockUpdates.SetBlock(target, BlocksRegistry.Air, Quiet);
                exchange.Setup(world);
                break;
        }
    }

    private static void Place(WorldData.World world, string state, ItemStack? first = null)
    {
        world.BlockUpdates.SetBlock(target, VanillaSulfurBlocks.ParseState(state), Quiet);
        if (first is not null && world.GetResidentBlockEntity(target) is ContainerBlockEntity container)
            container.Inventory.SetItem(0, first);
    }

    private static ItemStack Stack(string id, int count) => new(ItemsRegistry.Get(id), count);

    private static IBlockUseBehavior Behavior(BlockUse use) =>
        BlocksRegistry.GetUseItemOnBehavior(use.Block.RegistryId)!;

    private static BaseContainer? BlockInventory(WorldData.World world) =>
        (world.GetResidentBlockEntity(target) as ContainerBlockEntity)?.Inventory;

    private static BlockUse Read(RealServerContext context, ServerPlayer player) => new()
    {
        Server = context.Server,
        Level = context.World,
        Player = player,
        Position = target,
        Block = context.World.BlockUpdates.GetBlock(target),
        BlockEntity = context.World.GetResidentBlockEntity(target),
        Face = BlockFace.North,
        Cursor = slotZero,
        Hand = InteractionHand.MainHand,
        HandSlot = player.CurrentHeldItemSlot,
        Item = player.GetHeldItem(),
        UsedItem = player.GetHeldItem() is ItemStack item ? new ItemStack(item, item.Count) : null,
        Sequence = 1
    };

    // Every item there is, by id: in the player's inventory, held by the block (a potted plant, a container's items)
    // and lying around as item entities, once the drops queued meanwhile are in the level.
    private static async Task<Dictionary<string, int>> SettledCensusAsync(WorldData.World world, ServerPlayer player)
    {
        await world.RunQueuedEntityActionsAsync();
        return Census(world, player);
    }

    private static Dictionary<string, int> Census(WorldData.World world, ServerPlayer player, bool includeBlock = true)
    {
        var counts = new Dictionary<string, int>();
        void Count(ItemStack? stack)
        {
            if (stack is not { Count: > 0, IsAir: false })
                return;

            var id = stack.Holder.UnlocalizedName;
            counts[id] = counts.GetValueOrDefault(id) + stack.Count;
        }

        for (var slot = 0; slot < player.Inventory.Size; slot++)
            Count(player.Inventory.GetItem(slot));

        if (!includeBlock)
            return counts;

        Count(FlowerPots.ContentOf(world.BlockUpdates.GetBlock(target)));
        if (BlockInventory(world) is BaseContainer inventory)
        {
            for (var slot = 0; slot < inventory.Size; slot++)
                Count(inventory[slot]);
        }

        foreach (var entity in world.GetEntitiesInRange((VectorD)target, 6).OfType<ItemEntity>())
            Count(entity.Item);

        return counts;
    }

    private static Dictionary<string, int> Merge(Dictionary<string, int> first, Dictionary<string, int> second)
    {
        var merged = new Dictionary<string, int>(first);
        foreach (var (id, count) in second)
            merged[id] = merged.GetValueOrDefault(id) + count;

        return merged;
    }

    private static bool Same(Dictionary<string, int> first, Dictionary<string, int> second) =>
        first.Count == second.Count && first.All(pair => second.GetValueOrDefault(pair.Key) == pair.Value);

    private static string Describe(Dictionary<string, int> counts) =>
        string.Join(", ", counts.OrderBy(pair => pair.Key).Select(pair => $"{pair.Key} x{pair.Value}"));

    private static NbtCompound Without(NbtCompound tag, params string[] names)
    {
        foreach (var name in names)
            tag.Remove(name);

        return tag;
    }
}
