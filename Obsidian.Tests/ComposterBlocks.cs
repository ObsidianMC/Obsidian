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
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class ComposterBlocks
{
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
