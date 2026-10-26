using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Piston behavior on the live redstone pipeline (vanilla <c>PistonBaseBlock</c> / <c>PistonHeadBlock</c> /
/// <c>PistonMovingBlockEntity</c>): extension, retraction, sticky pulls, and immovable blocks.
/// </summary>
public class PistonTests
{
    private const int Y = 65;
    private static readonly Vector Piston = new(8, Y, 8);
    private static readonly Vector Power = new(7, Y, 8);
    private static readonly Vector Pushed = new(9, Y, 8);
    private static readonly Vector Beyond = new(10, Y, 8);

    private static RedstoneTestWorld NewWorld()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        return world;
    }

    private static async Task Settle(RedstoneTestWorld world, int ticks = 12)
    {
        for (var i = 0; i < ticks; i++)
            await world.Advance();
    }

    private static IBlock Block(RedstoneTestWorld world, Vector position) => world.GetLoadedBlock(position)!;

    [Fact]
    public async Task PoweredPistonExtendsAndPushesTheBlockInFront()
    {
        var world = NewWorld();
        await world.Place(Piston, BlocksRegistry.Get("minecraft:piston").WithProperty("facing", "east"));
        await world.Place(Pushed, BlocksRegistry.Get("minecraft:stone"));
        await world.Place(Power, BlocksRegistry.Get("minecraft:redstone_block"));
        await Settle(world);

        Assert.Equal("true", Block(world, Piston).GetProperty("extended"));
        Assert.True(Block(world, Pushed).Is(BlockIds.PistonHead));
        Assert.True(Block(world, Beyond).Is(BlockIds.Stone));
    }

    [Fact]
    public async Task RetractingPistonRemovesItsHeadAndLeavesThePushedBlock()
    {
        var world = NewWorld();
        await world.Place(Piston, BlocksRegistry.Get("minecraft:piston").WithProperty("facing", "east"));
        await world.Place(Pushed, BlocksRegistry.Get("minecraft:stone"));
        await world.Place(Power, BlocksRegistry.Get("minecraft:redstone_block"));
        await Settle(world);

        await world.Place(Power, BlocksRegistry.Get("minecraft:air"));
        await Settle(world);

        Assert.Equal("false", Block(world, Piston).GetProperty("extended"));
        Assert.True(Block(world, Pushed).IsAir);
        Assert.True(Block(world, Beyond).Is(BlockIds.Stone));
    }

    [Fact]
    public async Task StickyPistonPullsTheBlockBackOnRetraction()
    {
        var world = NewWorld();
        await world.Place(Piston, BlocksRegistry.Get("minecraft:sticky_piston").WithProperty("facing", "east"));
        await world.Place(Pushed, BlocksRegistry.Get("minecraft:stone"));
        await world.Place(Power, BlocksRegistry.Get("minecraft:redstone_block"));
        await Settle(world);

        await world.Place(Power, BlocksRegistry.Get("minecraft:air"));
        await Settle(world);

        Assert.Equal("false", Block(world, Piston).GetProperty("extended"));
        Assert.True(Block(world, Pushed).Is(BlockIds.Stone));
        Assert.True(Block(world, Beyond).IsAir);
    }

    [Fact]
    public async Task ObsidianIsImmovableSoThePistonStaysRetracted()
    {
        var world = NewWorld();
        await world.Place(Piston, BlocksRegistry.Get("minecraft:piston").WithProperty("facing", "east"));
        await world.Place(Pushed, BlocksRegistry.Get("minecraft:obsidian"));
        await world.Place(Power, BlocksRegistry.Get("minecraft:redstone_block"));
        await Settle(world);

        Assert.Equal("false", Block(world, Piston).GetProperty("extended"));
        Assert.True(Block(world, Pushed).Is(BlockIds.Obsidian));
    }
}
