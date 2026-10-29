using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.BlockEntities;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.ChunkData;
using Obsidian.Entities;
using Obsidian.API.ChunkData;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class ComposterBlocks
{
    // Vanilla's ComposterBlock.COMPOSTABLES: oak leaves raise the level a third of the time, and cake always.
    [Fact]
    public void TheChanceTableMatchesVanilla()
    {
        Assert.Equal(0.3f, Composters.ChanceOf(ItemIds.OakLeaves));
        Assert.Equal(1.0f, Composters.ChanceOf(ItemIds.Cake));
        Assert.Null(Composters.ChanceOf(ItemIds.Stone));
        Assert.Null(Composters.ChanceOf(ItemIds.BoneMeal));
    }

    // Vanilla's ComposterBlock.useItemOn: a compostable item in the hand is used up and raises the level by its chance.
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AnItemPutInRaisesTheLevelAndIsUsedUp()
    {
        await using var context = TestServerFactory.CreateServer($"composter-fill-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:composter", out var composter));

        var position = new Vector(8, 65, 8);
        await context.World.SetBlockAsync(position, composter.WithProperty("level", "3"), true);

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Filler", new VectorD(8.5, 66, 6.5));
        player.GameMode = GameMode.Survival;
        player.Yaw = 0;
        player.Pitch = 0;
        player.Inventory.SetItem(player.CurrentHeldItemSlot, new ItemStack(ItemsRegistry.Get(ItemIds.Cake), 2));

        await BlockUpdates.UseOn(position, BlockFace.Up).HandleAsync(context.Server, player);

        // Cake is always composted: the level rose by one and one cake was used up.
        Assert.Equal("4", context.World.BlockUpdates.GetBlock(position).GetProperty("level"));
        Assert.Equal(1, player.GetHeldItem()?.Count);
    }

    private static BaseContainer Inventory(World world, Vector position) =>
        Assert.IsAssignableFrom<ContainerBlockEntity>(
            world.GetBlockEntityAsync(position).AsTask().GetAwaiter().GetResult()).Inventory;

    // Vanilla's hopper: one item moves per transfer, not the whole stack.
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AHopperAboveMovesOneItemIntoAComposter()
    {
        await using var context = TestServerFactory.CreateServer($"composter-hopper-{Guid.NewGuid():N}");
        var world = context.World;
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
        }
        await WorldBlockEntities.JoinAsync(context);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:composter", out var composter));

        var hopper = new Vector(8, 66, 8);
        var target = new Vector(8, 65, 8);
        await world.SetBlockAsync(target, composter.WithProperty("level", "2"), true);
        await world.SetBlockAsync(hopper, BlocksRegistry.Hopper.WithProperty("facing", "down"), true);
        Inventory(world, hopper).SetItem(0, new ItemStack(ItemsRegistry.Get(ItemIds.Cake), 5));

        await world.DoWorldTickAsync();

        Assert.Equal("3", world.BlockUpdates.GetBlock(target).GetProperty("level"));
        Assert.Equal(4, Inventory(world, hopper)[0]?.Count);
    }

    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task SeedHopperUsesTheHopperMenuAndTransfersOnCooldown()
    {
        await using var context = TestServerFactory.CreateServer($"composter-seeds-{Guid.NewGuid():N}");
        var world = context.World;
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                WorldBlockEntities.AddEmptyChunk(world, chunkX, chunkZ);
        }
        await WorldBlockEntities.JoinAsync(context);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:composter", out var composter));
        var target = new Vector(8, 65, 8);
        var hopper = target + Vector.Up;
        await world.SetBlockAsync(target, composter, true);
        await world.SetBlockAsync(hopper, BlocksRegistry.Hopper.WithProperty("facing", "down"), true);
        var inventory = Inventory(world, hopper);
        var screen = new Obsidian.Net.Packets.Play.Clientbound.OpenScreenPacket(inventory, 1);
        Assert.Equal(Obsidian.Net.Packets.Play.Clientbound.WindowType.Hopper, screen.Type);
        inventory.SetItem(0, new ItemStack(ItemsRegistry.Get(ItemIds.WheatSeeds), 64));

        await world.DoWorldTickAsync();
        Assert.Equal(63, inventory[0]?.Count);
        Assert.Equal("1", world.BlockUpdates.GetBlock(target).GetProperty("level"));
        for (var tick = 0; tick < HopperBlockEntity.MoveItemSpeed - 1; tick++)
            await world.DoWorldTickAsync();
        Assert.Equal(63, inventory[0]?.Count);
        await world.DoWorldTickAsync();
        Assert.Equal(62, inventory[0]?.Count);
    }

    // Vanilla's ComposterBlock.onPlace and tick: a composter that fills to 7 schedules the tick that makes it ready.
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AFullComposterIsScheduledToBecomeReady()
    {
        await using var context = TestServerFactory.CreateServer($"composter-ready-tick-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:composter", out var composter));

        var position = new Vector(8, 65, 8);
        await context.World.SetBlockAsync(position, composter, true);
        var updates = context.World.BlockUpdates;
        lock (context.World.Fluids.TickLock)
            updates.SetBlock(position, composter.WithProperty("level", Composters.Full.ToString()));

        var full = updates.GetBlock(position);
        Assert.True(updates.HasScheduledTick(position, full));

        Assert.True(updates.RunScheduledTick(position, full.UnlocalizedName));
        Assert.Equal(Composters.Ready.ToString(), updates.GetBlock(position).GetProperty("level"));
    }

    // Vanilla's ComposterBlock.useWithoutItem: a ready composter gives its bone meal out and empties.
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task AReadyComposterGivesItsBoneMealAndEmpties()
    {
        await using var context = TestServerFactory.CreateServer($"composter-ready-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:composter", out var composter));

        var position = new Vector(8, 65, 8);
        await context.World.SetBlockAsync(position, composter.WithProperty("level", Composters.Ready.ToString()), true);

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Taker", new VectorD(8.5, 66, 6.5));
        player.GameMode = GameMode.Survival;
        player.Yaw = 0;
        player.Pitch = 0;

        await BlockUpdates.UseOn(position, BlockFace.Up).HandleAsync(context.Server, player);

        Assert.Equal("0", context.World.BlockUpdates.GetBlock(position).GetProperty("level"));
        var drop = Assert.Single(context.World.GetEntitiesInRange((VectorD)position, 4).OfType<ItemEntity>());
        Assert.Equal(ItemIds.BoneMeal, drop.Item.Holder.Id);
        Assert.Equal(1, drop.Item.Count);
    }

    // Vanilla's ComposterBlock.getContainer: from above the composter takes items while not full, and from below it
    // gives out bone meal once ready. Nothing else reaches it.
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task HoppersReachItsTopAndBottomByLevel()
    {
        await using var context = TestServerFactory.CreateServer($"composter-access-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:composter", out var composter));

        var position = new Vector(8, 65, 8);
        var level = context.World;
        await context.World.SetBlockAsync(position, composter.WithProperty("level", "2"), true);

        var top = level.GetItemAccess(position, BlockFace.Up);
        Assert.NotNull(top);
        Assert.Equal(1, top.SlotCount);
        Assert.Null(level.GetItemAccess(position, BlockFace.Down)?.GetItem(0));

        var cake = new ItemStack(ItemsRegistry.Get(ItemIds.Cake), 3);
        Assert.Equal(2, top.Insert(cake)?.Count);
        Assert.Equal("3", context.World.BlockUpdates.GetBlock(position).GetProperty("level"));

        await context.World.SetBlockAsync(position, composter.WithProperty("level", Composters.Ready.ToString()), true);
        var bottom = level.GetItemAccess(position, BlockFace.Down);
        Assert.NotNull(bottom);
        Assert.Equal(ItemIds.BoneMeal, bottom.GetItem(0)?.Holder.Id ?? -1);
        Assert.Equal(1, bottom.Extract(0, 1)?.Count);
        Assert.Equal("0", context.World.BlockUpdates.GetBlock(position).GetProperty("level"));
    }
}
