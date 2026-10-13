using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Events;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Breaking a two-block plant. The rules are vanilla's <c>DoublePlantBlock</c> (playerWillDestroy, preventDropFromBottomPart,
/// and the lower half's <c>half=lower</c> loot condition), checked against the 1.21.11 bytecode.
/// </summary>
public class DoublePlantBreakIntegration
{
    private static readonly Vector Spot = BlockBreakIntegration.Spot;

    private static async Task<(string[] Drops, string Lower, string Upper)> BreakAsync(string name, string half, GameMode mode, string test)
    {
        await using var context = TestServerFactory.CreateServer(test);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Breaker");
        player.GameMode = mode;

        var plant = BlocksRegistry.Get(name);
        await context.World.SetBlockAsync(Spot + Vector.Down, BlockStateProperties.GetState("minecraft:grass_block"), true);
        await context.World.SetBlockAsync(Spot, plant.WithProperty("half", "lower"), true);
        await context.World.SetBlockAsync(Spot + Vector.Up, plant.WithProperty("half", "upper"), true);

        var target = half == "lower" ? Spot : Spot + Vector.Up;
        await new MainEventHandler().OnBlockBreak(BlockBreakIntegration.CreateArgs(context, player, (await context.World.GetBlockAsync(target))!, target));

        var drops = context.World.GetEntitiesInRange(new VectorD(Spot.X + 0.5, Spot.Y + 0.5, Spot.Z + 0.5), 4f)
            .OfType<ItemEntity>()
            .Select(entity => $"{entity.Item.Holder.UnlocalizedName}*{entity.Item.Count}")
            .ToArray();
        return (drops,
            (await context.World.GetBlockAsync(Spot))!.UnlocalizedName,
            (await context.World.GetBlockAsync(Spot + Vector.Up))!.UnlocalizedName);
    }

    [Theory]
    [InlineData("minecraft:sunflower")]
    [InlineData("minecraft:lilac")]
    public async Task SurvivalBreakOfTheUpperHalfDropsTheFlowerFromTheLowerHalf(string name)
    {
        var (drops, lower, upper) = await BreakAsync(name, "upper", GameMode.Survival, "dp-survival-upper-" + name);

        // The upper half's loot needs half=lower, so the flower comes from the lower half, removed with the upper.
        Assert.Equal([name + "*1"], drops);
        Assert.Equal("minecraft:air", lower);
        Assert.Equal("minecraft:air", upper);
    }

    [Theory]
    [InlineData("minecraft:sunflower")]
    [InlineData("minecraft:lilac")]
    public async Task SurvivalBreakOfTheLowerHalfDropsOneFlower(string name)
    {
        var (drops, lower, upper) = await BreakAsync(name, "lower", GameMode.Survival, "dp-survival-lower-" + name);

        Assert.Equal([name + "*1"], drops);
        Assert.Equal("minecraft:air", lower);
        Assert.Equal("minecraft:air", upper);
    }

    [Theory]
    [InlineData("minecraft:sunflower")]
    [InlineData("minecraft:lilac")]
    public async Task CreativeBreakOfTheUpperHalfDropsNothing(string name)
    {
        var (drops, lower, upper) = await BreakAsync(name, "upper", GameMode.Creative, "dp-creative-upper-" + name);

        // preventDropFromBottomPart: the lower half goes silently, so no flower.
        Assert.Empty(drops);
        Assert.Equal("minecraft:air", lower);
        Assert.Equal("minecraft:air", upper);
    }

    [Theory]
    [InlineData("minecraft:sunflower")]
    [InlineData("minecraft:lilac")]
    public async Task CreativeBreakOfTheLowerHalfDropsNothing(string name)
    {
        var (drops, lower, upper) = await BreakAsync(name, "lower", GameMode.Creative, "dp-creative-lower-" + name);

        Assert.Empty(drops);
        Assert.Equal("minecraft:air", lower);
        Assert.Equal("minecraft:air", upper);
    }
}
