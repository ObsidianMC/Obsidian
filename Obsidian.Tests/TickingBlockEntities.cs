using Obsidian.API;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The level ticks only the block entities whose classes tick, from a list each chunk keeps beside its block entities
/// (see <see cref="Chunk.TickingBlockEntities"/>). These guard what that list risks: a block entity ticked from before
/// it was placed until after it's gone, and the upkeep every block entity still gets each tick.
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
        await world.SetBlockAsync(machine, BlocksRegistry.Hopper);
        var hopper = Assert.IsAssignableFrom<ContainerBlockEntity>(world.GetResidentBlockEntity(machine));
        spent = furnace.CookingSpent;
        await TickAsync(world, 2);

        Assert.Equal(spent, furnace.CookingSpent);
        Assert.True(hopper.Inventory[0]?.Is(ItemsRegistry.Cobblestone));

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

    [Fact(DisplayName = "A block entity another one's tick removes in the same pass isn't ticked",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task BlockEntityRemovedDuringThePassIsNotTicked()
    {
        await using var context = await OpenAsync("ticking-removed");
        var world = context.World;
        var first = machine;
        var second = machine + Vector.East * 2;

        // Two breakers, each removing the other on its tick: whichever ticks first, the other never does.
        await PlaceBreakerAsync(world, first, second);
        await PlaceBreakerAsync(world, second, first);
        await TickAsync(world, 3);

        var firstTicked = world.BlockUpdates.GetBlock(first + Vector.Up).Is(BlockIds.GoldBlock);
        var secondTicked = world.BlockUpdates.GetBlock(second + Vector.Up).Is(BlockIds.GoldBlock);

        Assert.NotEqual(firstTicked, secondTicked);
    }

    [Fact(DisplayName = "A container that doesn't tick still tells its comparator of items moved without a block update",
        Timeout = TestServerFactory.TestTimeout)]
    public async Task IdleContainerTellsItsComparator()
    {
        await using var context = await OpenAsync("ticking-idle-container");
        var world = context.World;
        var comparator = BlocksRegistry.GetDefault(BlockIds.Comparator).WithProperty("facing", "west");
        var dust = machine + Vector.East * 2;

        var chest = await PlaceAsync<ContainerBlockEntity>(world, machine, BlocksRegistry.Chest);
        await world.SetBlockAsync(machine + Vector.East, comparator, doBlockUpdate: true);
        await world.SetBlockAsync(dust, BlocksRegistry.RedstoneWire, doBlockUpdate: true);
        await TickAsync(world, 4);

        Assert.Equal("0", world.BlockUpdates.GetBlock(dust).GetProperty("power"));

        // Like a player's click: the items change outside the level's tick, with no block update.
        chest.Inventory.SetItem(0, new ItemStack(ItemsRegistry.Stone, 64));
        await TickAsync(world, 4);

        Assert.Equal("1", world.BlockUpdates.GetBlock(dust).GetProperty("power"));
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
        furnace.Inventory.SetItem(0, new ItemStack(ItemsRegistry.RawIron, 2));
        furnace.Inventory.SetItem(1, new ItemStack(ItemsRegistry.Coal, 1));

        return furnace;
    }

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
    /// Removes the block entity at <see cref="Other"/> on its tick, and marks that it ticked with a gold block above
    /// itself.
    /// </summary>
    private sealed class Breaker : BlockEntity
    {
        public Vector Other { get; init; }

        protected internal override async ValueTask TickAsync()
        {
            await this.Level!.SetBlockAsync(this.BlockPosition + Vector.Up, BlocksRegistry.GoldBlock);
            await this.Level.SetBlockAsync(this.Other, BlocksRegistry.Air);
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
