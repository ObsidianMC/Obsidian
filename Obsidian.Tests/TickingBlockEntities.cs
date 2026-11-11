using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Redstone;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The level's block entity pass ticks only the block entities whose classes tick (<c>BlockEntity.Ticks</c>), and
/// keeps up the others only when they have something to do, in one walk over each chunk's block entities in their
/// order. These guard what that risks: a block entity ticked when it shouldn't be or not ticked when it should, and
/// the upkeep of those that don't tick happening where it did in the walk.
/// </summary>
public sealed class TickingBlockEntities
{
    private static readonly Vector machine = new(4, BlockUpdates.Ground + 1, 4);

    [Fact(DisplayName = "A block entity ticks while it's in place, replaced or reloaded, and never once it's gone",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BlockEntitiesTickExactlyWhileInPlace()
    {
        await using var context = await OpenAsync("ticking-in-place");
        var world = context.World;
        var chest = await PlaceAsync<ContainerBlockEntity>(world, machine + Vector.Up, BlocksRegistry.Chest);
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Cobblestone, 1));

        // Placed, a furnace cooks.
        var furnace = await PlaceFurnaceAsync(world);
        await TickAsync(world, 1);
        var spent = furnace.CookingSpent;
        await TickAsync(world, 2);

        Assert.Equal(spent + 2, furnace.CookingSpent);

        // Replaced by a hopper, the furnace stops and the hopper takes the chest's item.
        // Don't scatter leftover input into the new hopper: inside contact can legitimately collect it before
        // the block-entity pass, putting the hopper on cooldown instead of pulling from the chest.
        furnace.Inventory.SetItem(0, null);
        await world.SetBlockAsync(machine, BlocksRegistry.Hopper);
        var hopper = Assert.IsAssignableFrom<ContainerBlockEntity>(world.GetResidentBlockEntity(machine));
        spent = furnace.CookingSpent;
        await TickAsync(world, 2);

        Assert.Equal(spent, furnace.CookingSpent);
        Assert.True(hopper.Inventory[0]?.Is(ItemsRegistry.Cobblestone),
            string.Join(", ", hopper.Inventory.Select(stack => stack?.Holder.UnlocalizedName ?? "air")));

        // Removed, the hopper takes nothing more.
        await world.SetBlockAsync(machine, BlocksRegistry.Air);
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Cobblestone, 1));
        await TickAsync(world, 2);

        Assert.Equal(1, chest.Inventory[0]?.Count);

        // Saved and loaded again, a furnace goes on cooking where it was.
        furnace = await PlaceFurnaceAsync(world);
        await TickAsync(world, 2);
        spent = furnace.CookingSpent;
        var reloaded = await ReloadAsync(world);
        await TickAsync(world, 2);

        Assert.NotSame(furnace, reloaded);
        Assert.Equal(spent + 2, reloaded.CookingSpent);
    }

    [Fact(DisplayName = "A block entity a plugin puts in a chunk's dictionary directly ticks in place of the one there",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BlockEntityPutInTheDictionaryDirectlyTicks()
    {
        await using var context = await OpenAsync("ticking-direct");
        var world = context.World;
        var chunk = Assert.IsType<Chunk>(world.GetLoadedChunk(0, 0));
        var furnace = await PlaceFurnaceAsync(world);
        await TickAsync(world, 1);

        // Like a plugin writing the public dictionary: no level or chunk method hears of it.
        var key = chunk.BlockEntities.Single(entry => ReferenceEquals(entry.Value, furnace)).Key;
        var replacement = Assert.IsType<FurnaceBlockEntity>(BlockEntityNbt.Create(furnace.Id, machine));
        Fill(replacement);
        chunk.BlockEntities[key] = replacement;

        var spent = furnace.CookingSpent;
        await TickAsync(world, 2);

        Assert.Equal(spent, furnace.CookingSpent);
        Assert.Equal(2, replacement.CookingSpent);
    }

    [Fact(DisplayName = "A block entity another one's tick replaces in the same pass isn't ticked",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BlockEntityReplacedDuringThePassIsNotTicked()
    {
        await using var context = await OpenAsync("ticking-replaced");
        var world = context.World;
        var first = machine;
        var second = machine + Vector.East * 2;

        // Two breakers, each replacing the other on its tick without marking it removed: whichever ticks first, the
        // other never does.
        await PlaceBreakerAsync(world, first, second);
        await PlaceBreakerAsync(world, second, first);
        await TickAsync(world, 3);

        var firstTicked = world.BlockUpdates.GetBlock(first + Vector.Up).Is(BlockIds.GoldBlock);
        var secondTicked = world.BlockUpdates.GetBlock(second + Vector.Up).Is(BlockIds.GoldBlock);

        Assert.NotEqual(firstTicked, secondTicked);
    }

    [Fact(DisplayName = "A container that doesn't tick tells its comparator where it is in the pass, before later ticks",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task IdleContainerTellsItsComparatorInOrder()
    {
        await using var context = await OpenAsync("ticking-idle-container");
        var world = context.World;
        var chunk = Assert.IsType<Chunk>(world.GetLoadedChunk(0, 0));
        var chestAt = machine + Vector.Up;
        var dust = chestAt + Vector.East * 2;

        var chest = await PlaceAsync<ContainerBlockEntity>(world, chestAt, BlocksRegistry.Chest);
        await PlaceComparatorAsync(world, chestAt);

        // Like a player's click: the items change outside the level's tick, with no block update.
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Stone, 64));
        await TickAsync(world, 4);

        Assert.Equal("1", Power(world, dust));

        chest.Inventory.SetItem(0, null);
        await TickAsync(world, 4);

        Assert.Equal("0", Power(world, dust));

        // A hopper under the chest, after it in the chunk's block entities, pulls from it.
        var hopper = await PlaceAsync<ContainerBlockEntity>(world, machine, BlocksRegistry.Hopper);
        await TickAsync(world, 2);
        var order = chunk.BlockEntities.Select(entry => entry.Value).ToList();

        Assert.True(order.IndexOf(chest) < order.IndexOf(hopper));

        // An item put in outside the tick: the chest's place in the pass comes first, so its comparator hears of the
        // item and schedules its update before the hopper takes it.
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Stone, 1));
        await TickAsync(world, 1);

        Assert.True(hopper.Inventory[0]?.Is(ItemsRegistry.Stone));

        // Another while the hopper waits out its cooldown: the update already scheduled reads it, two ticks after
        // the first item. Had the chest been kept up after the hopper, nothing would have been scheduled for the
        // first, and the dust would light a tick later.
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Stone, 1));
        await TickAsync(world, 1);

        Assert.Equal("0", Power(world, dust));

        await TickAsync(world, 1);

        Assert.Equal("1", Power(world, dust));
    }

    // A server whose nine chunks around the origin have a stone floor, the middle one block-ticking under a ticket.
    private static async Task<TickingContext> OpenAsync(string name)
    {
        var context = TestServerFactory.CreateServer($"{name}-{Guid.NewGuid():N}");
        context.World.RecipeSource = () => context.Server.RecipeRegistry.Current;
        await BlockUpdates.AddChunksAsync(context.World);

        return new TickingContext(context, context.World.AddChunkTicket(0, 0));
    }

    private static async Task<T> PlaceAsync<T>(World world, Vector position, IBlock block) where T : class
    {
        await world.SetBlockAsync(position, block);

        return Assert.IsAssignableFrom<T>(world.GetResidentBlockEntity(position));
    }

    // A furnace with coal and two raw iron, which it starts cooking on its first tick.
    private static async Task<FurnaceBlockEntity> PlaceFurnaceAsync(World world)
    {
        var furnace = await PlaceAsync<FurnaceBlockEntity>(world, machine, BlocksRegistry.Furnace);
        Fill(furnace);

        return furnace;
    }

    private static void Fill(FurnaceBlockEntity furnace)
    {
        furnace.Inventory.SetItem(0, new ItemStack(ItemsRegistry.RawIron, 2));
        furnace.Inventory.SetItem(1, new ItemStack(ItemsRegistry.Coal, 1));
    }

    // A comparator reading the container at position from the east, on stone, and dust beyond it.
    private static async Task PlaceComparatorAsync(World world, Vector position)
    {
        var comparator = BlocksRegistry.GetDefault(BlockIds.Comparator).WithProperty("facing", "west");
        var reading = position + Vector.East;
        var dust = reading + Vector.East;

        await world.SetBlockAsync(reading + Vector.Down, BlocksRegistry.Stone);
        await world.SetBlockAsync(dust + Vector.Down, BlocksRegistry.Stone);
        await world.SetBlockAsync(reading, comparator, doBlockUpdate: true);
        await world.SetBlockAsync(dust, BlocksRegistry.RedstoneWire, doBlockUpdate: true);
        await TickAsync(world, 4);
    }

    private static string? Power(World world, Vector dust) => world.BlockUpdates.GetBlock(dust).GetProperty("power");

    private static async Task PlaceBreakerAsync(World world, Vector position, Vector other)
    {
        await world.SetBlockAsync(position, BlocksRegistry.GetDefault(BlockIds.OakSign));
        await world.SetBlockEntity(position, new Breaker { Other = other });
    }

    // Saves and unloads the middle chunk, then loads it again: the furnace in it is the one loaded from its save.
    private static async Task<FurnaceBlockEntity> ReloadAsync(World world)
    {
        var region = Assert.IsType<Region>(world.LoadRegionByChunk(0, 0));
        await region.UnloadChunk(0, 0);
        await world.GetChunkAsync(0, 0, scheduleGeneration: false);

        return Assert.IsType<FurnaceBlockEntity>(world.GetResidentBlockEntity(machine));
    }

    private static async Task TickAsync(World world, int ticks)
    {
        for (var tick = 0; tick < ticks; tick++)
            await world.DoWorldTickAsync();
    }

    /// <summary>
    /// On its tick, marks that it ticked with a gold block above itself, and replaces the block entity at
    /// <see cref="Other"/> with a sign's through <c>IRedstoneLevel.SetBlockEntityAt</c>, which doesn't mark the one
    /// replaced removed.
    /// </summary>
    private sealed class Breaker : BlockEntity
    {
        public Vector Other { get; init; }

        protected internal override async ValueTask TickAsync()
        {
            var level = this.Level!;
            await level.SetBlockAsync(this.BlockPosition + Vector.Up, BlocksRegistry.GoldBlock);

            var sign = BlockEntityNbt.Create(this.Id, this.Other);
            ((IRedstoneLevel)level).SetBlockEntityAt(this.Other, sign);
        }
    }

    private sealed class TickingContext(RealServerContext context, IDisposable ticket) : IAsyncDisposable
    {
        public World World => context.World;

        public async ValueTask DisposeAsync()
        {
            ticket.Dispose();
            await context.DisposeAsync();
        }
    }
}
