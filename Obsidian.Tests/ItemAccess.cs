using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Containers;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Item access: vanilla's containers give their slots by side under vanilla's rules, and hoppers move items through
/// it with vanilla's timings.
/// </summary>
// Loading plugins merges their tags into vanilla's, which PluginTagFiles changes too.
[Collection(nameof(PluginTagFiles))]
public sealed class ItemAccess
{
    private static readonly Vector furnace = new(1, 70, 1);
    private static readonly Vector stand = new(3, 70, 1);
    private static readonly Vector box = new(5, 70, 1);
    private static readonly Vector leftChest = new(8, 70, 1);
    private static readonly Vector rightChest = new(9, 70, 1);

    // Each row inserts a stack through a side, simulated and then for real: how many go in, and what the container
    // slot named holds after. Vanilla's WorldlyContainer rules for furnaces, brewing stands and shulker boxes.
    private static readonly List<InsertRow> inserts =
    [
        new("A furnace's top takes ingredients", furnace, BlockFace.Up, new(ItemsRegistry.IronOre, 5), 5, 0, 5),
        new("A furnace's side takes fuel", furnace, BlockFace.North, new(ItemsRegistry.Coal, 5), 5, 1, 5),
        new("A furnace's side refuses what doesn't burn", furnace, BlockFace.North, new(ItemsRegistry.IronOre), 0),
        new("A furnace's bottom skips the result for the fuel", furnace, BlockFace.Down, new(ItemsRegistry.Coal), 1,
            1, 6),
        new("A brewing stand's side takes a bottle a slot", stand, BlockFace.East, new(ItemsRegistry.GlassBottle, 5),
            3, 0, 1),
        new("A brewing stand's top takes ingredients", stand, BlockFace.Up, new(ItemsRegistry.NetherWart, 2), 2, 3, 2),
        new("A shulker box refuses shulker boxes", box, BlockFace.Up, new(ItemsRegistry.ShulkerBox), 0),
        new("A double chest fills its right half first", leftChest, BlockFace.Up, new(ItemsRegistry.Stone, 70), 70,
            0, 64)
    ];

    [Fact(DisplayName = "Item access follows vanilla's side and slot rules, and simulating changes nothing",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task ContainersFollowVanillaSideRules()
    {
        await using var context = TestServerFactory.CreateServer($"item-access-{Guid.NewGuid():N}");
        var world = context.World;
        WorldBlockEntities.AddEmptyChunk(world);

        await world.SetBlockAsync(furnace, BlocksRegistry.Furnace);
        await world.SetBlockAsync(stand, BlocksRegistry.BrewingStand);
        await world.SetBlockAsync(box, BlocksRegistry.ShulkerBox);
        await world.SetBlockAsync(leftChest, BlocksRegistry.Chest.WithProperty("type", "left"));
        await world.SetBlockAsync(rightChest, BlocksRegistry.Chest.WithProperty("type", "right"));

        foreach (var (row, position, side, stack, moved, slot, inSlot) in inserts)
        {
            var access = world.GetItemAccess(position, side);
            Assert.NotNull(access);

            var target = position == leftChest ? rightChest : position;
            var inventory = Inventory(world, target);
            var revision = inventory.Revision;

            var planned = access.Insert(stack, simulate: true);
            Assert.True(revision == inventory.Revision, row);

            var left = access.Insert(stack);
            Assert.True(stack.Count - (left?.Count ?? 0) == moved, row);
            Assert.True(planned?.Count == left?.Count, row);

            if (moved > 0)
                Assert.True(inventory[slot]?.Count == inSlot, row);
        }

        // A double chest's halves are one 54-slot inventory.
        Assert.Equal(54, world.GetItemAccess(rightChest, BlockFace.Up)?.SlotCount);

        // From below, a furnace gives up its result and then its fuel, which only a bucket leaves through.
        var bottom = world.GetItemAccess(furnace, BlockFace.Down)!;
        Assert.Null(bottom.Extract(1, 1));
        Assert.Equal(6, world.GetItemAccess(furnace, BlockFace.West)!.Extract(0, 64, simulate: true)?.Count);

        // A removed container's access takes and gives nothing.
        var chest = world.GetItemAccess(leftChest, BlockFace.Up)!;
        await world.SetBlockAsync(leftChest, BlocksRegistry.Air);
        await world.SetBlockAsync(rightChest, BlocksRegistry.Air);

        var stone = new ItemStack(ItemsRegistry.Stone);
        Assert.Same(stone, chest.Insert(stone));
        Assert.Null(chest.Extract(0, 1));
    }

    [Fact(DisplayName = "Hoppers move a chest's items to a furnace at vanilla's pace",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task HopperChainMovesItemsAtVanillaPace()
    {
        await using var context = TestServerFactory.CreateServer($"hoppers-{Guid.NewGuid():N}");
        var world = context.World;
        await AddTickingChunksAsync(world);
        await WorldBlockEntities.JoinAsync(context);

        // A chest over a hopper, which feeds a second hopper beside it, which feeds a furnace's side (its fuel).
        var chest = new Vector(4, 72, 4);
        var first = new Vector(4, 71, 4);
        var second = new Vector(5, 71, 4);
        var target = new Vector(6, 71, 4);

        await world.SetBlockAsync(chest, BlocksRegistry.Chest);
        await world.SetBlockAsync(first, BlocksRegistry.Hopper.WithProperty("facing", "east"));
        await world.SetBlockAsync(second, BlocksRegistry.Hopper.WithProperty("facing", "east"));
        await world.SetBlockAsync(target, BlocksRegistry.Furnace);
        Inventory(world, chest).SetItem(13, new ItemStack(ItemsRegistry.Coal, 3));

        // Reasoned from vanilla's HopperBlockEntity: the first hopper pulls on its first tick and every 8 ticks after;
        // the second, filled from empty, waits 8 ticks less the one it ticks in, whichever ticks first.
        var fuelByTick = new Dictionary<int, int>
        {
            [15] = 0,
            [16] = 1,
            [23] = 1,
            [24] = 2,
            [32] = 3
        };

        for (var tick = 1; tick <= 32; tick++)
        {
            await world.DoWorldTickAsync();

            if (tick == 1)
                Assert.Equal(2, Inventory(world, chest)[13]?.Count);

            if (fuelByTick.TryGetValue(tick, out var fuel))
                Assert.Equal(fuel, Inventory(world, target)[1]?.Count ?? 0);
        }

        Assert.False(Inventory(world, chest).HasItems());
        Assert.False(Inventory(world, first).HasItems());
        Assert.False(Inventory(world, second).HasItems());

        // Powered, the first hopper stops pulling; a dropper powered from above puts an item into the chest it faces
        // four ticks later.
        var dropper = new Vector(8, 71, 4);
        var dropped = new Vector(9, 71, 4);

        await world.SetBlockAsync(dropper, BlocksRegistry.Dropper.WithProperty("facing", "east"));
        await world.SetBlockAsync(dropped, BlocksRegistry.Chest);
        Inventory(world, dropper).SetItem(4, new ItemStack(ItemsRegistry.Diamond, 2));
        Inventory(world, chest).SetItem(0, new ItemStack(ItemsRegistry.Coal, 1));

        await world.SetBlockAsync(first + Vector.West, BlocksRegistry.RedstoneBlock, doBlockUpdate: true);
        await world.SetBlockAsync(dropper + Vector.Up, BlocksRegistry.RedstoneBlock, doBlockUpdate: true);

        for (var tick = 1; tick <= 4; tick++)
        {
            Assert.False(Inventory(world, dropped).HasItems());
            await world.DoWorldTickAsync();
        }

        Assert.Equal(1, Inventory(world, dropped)[0]?.Count);
        Assert.Equal(1, Inventory(world, dropper)[4]?.Count);
        Assert.Equal(1, Inventory(world, chest)[0]?.Count);
        Assert.Equal("false", (await world.GetBlockAsync(first))?.GetProperty("enabled"));
    }

    private sealed record InsertRow(
        string Row,
        Vector Position,
        BlockFace Side,
        ItemStack Stack,
        int Moved,
        int Slot = 0,
        int InSlot = 0);

    // Empty chunks around the origin, loaded as the level loads them, so that their block ticks run.
    private static async Task AddTickingChunksAsync(World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
        }

        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                await world.GetChunkAsync(chunkX, chunkZ, scheduleGeneration: false);
        }
    }

    private static BaseContainer Inventory(World world, Vector position) =>
        Assert.IsAssignableFrom<ContainerBlockEntity>(
            world.GetBlockEntityAsync(position).AsTask().GetAwaiter().GetResult()).Inventory;
}
