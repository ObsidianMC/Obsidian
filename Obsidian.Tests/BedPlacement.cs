using Obsidian.API;
using Obsidian.API.ChunkData;
using Obsidian.ChunkData;
using Obsidian.WorldData;
using Obsidian.Tests.Fakes;
using System;
using System.Threading.Tasks;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Xunit;

namespace Obsidian.Tests;

public class BedPlacement
{
    // Vanilla's BedBlock: the foot goes where the player places it, facing the way they look, and the head in front.
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PlacingABedPlacesItsHeadInFrontOfItsFoot()
    {
        await using var context = TestServerFactory.CreateServer($"bed-placement-{Guid.NewGuid():N}");
        IChunk chunk = new Chunk(0, 0);
        ((Chunk)chunk).SetChunkStatus(ChunkGenStage.full);
        context.World.LoadRegionByChunk(0, 0).SetChunk((Chunk)chunk);

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Sleeper", new VectorD(8.5, 65, 6.5));
        // Facing south, along +z.
        player.Yaw = 0;
        player.Pitch = 0;

        var foot = new Vector(8, 65, 8);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:red_bed", out var bed));
        var updates = context.World.BlockUpdates;

        IBlock? placed;
        lock (context.World.Fluids.TickLock)
            placed = VanillaPlacement.GetState(updates, bed, foot, player, BlockFace.Up, new VectorD(0.5, 1, 0.5), false);

        Assert.NotNull(placed);
        Assert.Equal("south", placed.GetProperty("facing"));
        Assert.Equal("foot", placed.GetProperty("part"));

        await context.World.SetBlockAsync(foot, placed, true);
        lock (context.World.Fluids.TickLock)
            VanillaPlacement.OnPlacedByPlayer(updates, foot, placed);

        Assert.Equal(placed.GetHashCode(), chunk.GetBlock(foot).GetHashCode());
        var head = chunk.GetBlock(foot + Vector.South);
        Assert.Equal(bed.RegistryId, head.RegistryId);
        Assert.Equal("head", head.GetProperty("part"));
        Assert.Equal("south", head.GetProperty("facing"));
        Assert.Equal("foot", chunk.GetBlock(foot).GetProperty("part"));

        // A bed whose head has no room isn't placed.
        chunk.SetBlock(new Vector(9, 65, 9), BlocksRegistry.Stone);
        lock (context.World.Fluids.TickLock)
            Assert.Null(VanillaPlacement.GetState(updates, bed, new Vector(9, 65, 8), player, BlockFace.Up,
                new VectorD(0.5, 1, 0.5), false));
    }
}
