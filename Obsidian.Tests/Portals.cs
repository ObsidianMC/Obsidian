using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.WorldData.Portals;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public class Portals
{
    [Theory]
    [InlineData("x", 2, 3)]
    [InlineData("z", 21, 21)]
    public async Task NetherFramesAllowMissingCorners(string axis, int width, int height)
    {
        var blocks = Frame(axis, width, height);
        var shape = await NetherPortalShape.FindAsync(Read, new Vector(0, 64, 0), axis, -64);
        Assert.NotNull(shape);
        Assert.Equal(width, shape.Value.Width);
        Assert.Equal(height, shape.Value.Height);
        Assert.Equal(0, shape.Value.PortalBlocks);

        ValueTask<IBlock?> Read(Vector position) => new(blocks.GetValueOrDefault(position, BlocksRegistry.Air));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(22, 3)]
    [InlineData(2, 2)]
    [InlineData(2, 22)]
    public async Task RejectsFramesOutsideVanillaLimits(int width, int height)
    {
        var blocks = Frame("x", width, height);
        Assert.Null(await NetherPortalShape.FindAsync(Read, new Vector(0, 64, 0), "x", -64));
        ValueTask<IBlock?> Read(Vector position) => new(blocks.GetValueOrDefault(position, BlocksRegistry.Air));
    }

    [Fact]
    public async Task BrokenFrameAndObstructedInteriorAreRejected()
    {
        var blocks = Frame("x", 2, 3);
        blocks.Remove(new Vector(0, 63, 0));
        Assert.Null(await NetherPortalShape.FindAsync(Read, new Vector(0, 64, 0), "x", -64));
        blocks = Frame("x", 2, 3);
        blocks[new Vector(1, 65, 0)] = BlocksRegistry.Get(Material.Stone);
        Assert.Null(await NetherPortalShape.FindAsync(Read, new Vector(0, 64, 0), "x", -64));
        ValueTask<IBlock?> Read(Vector position) => new(blocks.GetValueOrDefault(position, BlocksRegistry.Air));
    }

    [Fact]
    public async Task ExistingPortalBlocksAreCounted()
    {
        var blocks = Frame("x", 2, 3);
        blocks[new Vector(0, 64, 0)] = BlocksRegistry.Get(Material.NetherPortal);
        var shape = await NetherPortalShape.FindAsync(Read, new Vector(1, 66, 0), "x", -64);
        Assert.Equal(new Vector(0, 64, 0), shape!.Value.Origin);
        Assert.Equal(1, shape.Value.PortalBlocks);
        ValueTask<IBlock?> Read(Vector position) => new(blocks.GetValueOrDefault(position, BlocksRegistry.Air));
    }

    [Fact]
    public async Task EndRingRequiresAllEyesAndInwardFacingFrames()
    {
        var blocks = new Dictionary<Vector, IBlock>();
        var origin = new Vector(0, 64, 0);
        foreach (var (position, facing) in EndPortalFrames.Ring(origin))
            blocks[position] = BlocksRegistry.Get(Material.EndPortalFrame).WithProperty("eye", true).WithProperty("facing", facing);
        Assert.True(await EndPortalFrames.IsCompleteAsync(Read, origin));
        var north = origin + new Vector(0, 0, -1);
        blocks[north] = blocks[north].WithProperty("facing", "north");
        Assert.False(await EndPortalFrames.IsCompleteAsync(Read, origin));
        blocks[north] = blocks[north].WithProperty("facing", "south").WithProperty("eye", false);
        Assert.False(await EndPortalFrames.IsCompleteAsync(Read, origin));
        ValueTask<IBlock?> Read(Vector position) => new(blocks.GetValueOrDefault(position, BlocksRegistry.Air));
    }

    [Fact]
    public void ExitCollisionUsesSlabShapeInsteadOfWholeBlock()
    {
        var slab = BlocksRegistry.Get(Material.StoneSlab).WithProperty("type", "bottom");
        IBlock Read(Vector position) => position == new Vector(0, 64, 0) ? slab : BlocksRegistry.Air;
        var exit = PortalCollision.FindFreePosition(Read, new VectorD(0.5, 64.2, 0.5), 0.6, 1.8);
        Assert.Equal(64.5, exit.Y, 6);
        Assert.Equal(0.5, exit.X, 6);
        Assert.Equal(0.5, exit.Z, 6);
    }

    [Fact]
    public void UnobstructedExitDoesNotMove()
    {
        var position = new VectorD(-10.25, 70.5, 3.75);
        Assert.Equal(position, PortalCollision.FindFreePosition(_ => BlocksRegistry.Air, position, 0.6, 1.8));
    }

    [Fact]
    public void ObstructedExitMovesToNearestFreePosition()
    {
        IBlock Read(Vector position) => position == new Vector(0, 64, 0) ? BlocksRegistry.Get(Material.Stone) : BlocksRegistry.Air;
        var exit = PortalCollision.FindFreePosition(Read, new VectorD(0.5, 64, 0.5), 0.6, 1.8);
        var distance = Math.Sqrt(Math.Pow(exit.X - 0.5, 2) + Math.Pow(exit.Y - 64, 2) + Math.Pow(exit.Z - 0.5, 2));
        Assert.Equal(0.8, distance, 6);
    }

    private static Dictionary<Vector, IBlock> Frame(string axis, int width, int height)
    {
        var blocks = new Dictionary<Vector, IBlock>();
        var step = axis == "x" ? new Vector(1, 0, 0) : new Vector(0, 0, 1);
        var origin = new Vector(0, 64, 0);
        for (var x = 0; x < width; x++)
        {
            blocks[origin + step * x + Vector.Down] = BlocksRegistry.Get(Material.Obsidian);
            blocks[origin + step * x + Vector.Up * height] = BlocksRegistry.Get(Material.Obsidian);
        }
        for (var y = 0; y < height; y++)
        {
            blocks[origin - step + Vector.Up * y] = BlocksRegistry.Get(Material.Obsidian);
            blocks[origin + step * width + Vector.Up * y] = BlocksRegistry.Get(Material.Obsidian);
        }
        return blocks;
    }
}
