using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

public class TargetBlockTests
{
    private static readonly Vector Target = new(10, 64, 8);
    private static readonly Vector Lamp = new(11, 64, 8);

    private static async Task<RedstoneTestWorld> BuildAsync()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        await world.Place(Target, BlocksRegistry.Get("minecraft:target"));
        await world.Place(Lamp, BlocksRegistry.Get("minecraft:redstone_lamp"));
        return world;
    }

    private static int Power(RedstoneTestWorld world) => int.Parse(world.GetLoadedBlock(Target)!.GetProperty("power"));

    [Theory]
    [InlineData(0.5, 0.5, BlockFace.West, 15)]
    [InlineData(0.25, 0.5, BlockFace.West, 8)]   // offset 0.25 in y gives 7.5, which rounds up (Mth.ceil) to 8
    [InlineData(0.0, 0.0, BlockFace.West, 1)]    // a corner is the weakest hit that still counts
    [InlineData(0.5, 0.0, BlockFace.Up, 1)]      // on a top face, the offset is x and z, so z 0 is an edge
    [InlineData(0.5, 0.25, BlockFace.Up, 8)]
    public void StrengthDependsOnHowFarTheHitIsFromTheFaceCentre(double inFaceA, double inFaceB, BlockFace face, int expected)
    {
        // Hits on the west or east face vary in y and z; on a top or bottom face, in x and z; on a north or south face, in x and y.
        var location = face switch
        {
            BlockFace.West or BlockFace.East => new VectorD(10.0, 64 + inFaceA, 8 + inFaceB),
            BlockFace.Up or BlockFace.Down => new VectorD(10 + inFaceA, 65.0, 8 + inFaceB),
            _ => new VectorD(10 + inFaceA, 64 + inFaceB, 8.0),
        };

        Assert.Equal(expected, TargetBlock.StrengthAt(location, face));
    }

    [Fact]
    public void AnArrowEnteringFromTheWestEntersTheBlockOnItsWestFace()
    {
        var (block, face) = Arrow.EnteredBlockFace(new VectorD(3.0, 64.5, 1.25), new VectorD(0.6, 0, 0));

        Assert.Equal(new Vector(3, 64, 1), block);
        Assert.Equal(BlockFace.West, face);
    }

    [Fact]
    public void AnArrowEnteringFromTheEastEntersTheBlockWestOfTheBoundary()
    {
        // Moving west, the arrow stops on x = 5, the east face of block 4.
        var (block, face) = Arrow.EnteredBlockFace(new VectorD(5.0, 64.5, 1.5), new VectorD(-0.6, 0, 0));

        Assert.Equal(new Vector(4, 64, 1), block);
        Assert.Equal(BlockFace.East, face);
    }

    [Fact]
    public void AnArrowFallingOntoABlockEntersThroughItsTopFace()
    {
        var (block, face) = Arrow.EnteredBlockFace(new VectorD(3.5, 65.0, 1.5), new VectorD(0, -0.5, 0));

        Assert.Equal(new Vector(3, 64, 1), block);
        Assert.Equal(BlockFace.Up, face);
    }

    [Fact]
    public async Task AHitSetsThePowerAndTheLampFollows()
    {
        var world = await BuildAsync();

        await world.Advance(arrowHits: [new ArrowHit(Target, new VectorD(10.0, 64.5, 8.25), BlockFace.West)]);

        Assert.Equal(8, Power(world));
        Assert.Equal("true", world.GetLoadedBlock(Lamp)!.GetProperty("lit"));
    }

    [Fact]
    public async Task APulseEndsTwentyTicksAfterTheHit()
    {
        var world = await BuildAsync();
        await world.Advance(arrowHits: [new ArrowHit(Target, new VectorD(10.0, 64.5, 8.5), BlockFace.West)]);

        for (var tick = 0; tick < 19; tick++)
            await world.Advance();
        Assert.Equal(15, Power(world));

        await world.Advance();
        Assert.Equal(0, Power(world));
    }

    [Fact]
    public async Task AHitWhileAPulseIsPendingDoesNothing()
    {
        var world = await BuildAsync();
        await world.Advance(arrowHits: [new ArrowHit(Target, new VectorD(10.0, 64.5, 8.25), BlockFace.West)]);
        for (var tick = 0; tick < 3; tick++)
            await world.Advance();

        await world.Advance(arrowHits: [new ArrowHit(Target, new VectorD(10.0, 64.5, 8.5), BlockFace.West)]);

        Assert.Equal(8, Power(world));
    }

    [Fact]
    public async Task AHitOnAnotherBlockDoesNothing()
    {
        var world = await BuildAsync();
        await world.Place(new Vector(10, 64, 9), BlocksRegistry.Get("minecraft:stone"));

        await world.Advance(arrowHits: [new ArrowHit(new Vector(10, 64, 9), new VectorD(10.0, 64.5, 9.5), BlockFace.West)]);

        Assert.Equal(0, Power(world));
    }
}
