using Obsidian.API;
using Obsidian.API.ChunkData;
using Obsidian.ChunkData;
using Obsidian.WorldData;
using Obsidian.Tests.Fakes;
using System;
using System.Threading.Tasks;
using Obsidian.API.Blocks;
using Obsidian.Registries;
using Xunit;

namespace Obsidian.Tests;

public class SlabPlacement
{
    // Vanilla's SlabBlock: a slab placed on a slab of the same kind fills the other half, while a different kind doesn't.
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task PlacingASlabOnItsOpenSideMakesADoubleSlab()
    {
        await using var context = TestServerFactory.CreateServer($"slab-placement-{Guid.NewGuid():N}");
        IChunk chunk = new Chunk(0, 0);
        ((Chunk)chunk).SetChunkStatus(ChunkGenStage.full);
        context.World.LoadRegionByChunk(0, 0).SetChunk((Chunk)chunk);

        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, "Mason", new VectorD(8.5, 66, 6.5));
        player.Yaw = 0;
        player.Pitch = 0;

        var position = new Vector(8, 65, 8);
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:oak_slab", out var slab));
        Assert.True(BlocksRegistry.TryGetDefault("minecraft:stone_slab", out var stoneSlab));
        var bottom = slab.WithProperty("type", "bottom").WithProperty("waterlogged", false);
        var top = slab.WithProperty("type", "top").WithProperty("waterlogged", false);
        await context.World.SetBlockAsync(position, bottom, true);
        var updates = context.World.BlockUpdates;

        // The open top of a bottom slab: a slab of the same kind placed there makes a double slab.
        IBlock? placed;
        lock (context.World.Fluids.TickLock)
            placed = VanillaPlacement.GetState(updates, slab, position, player, BlockFace.Up, new VectorD(0.5, 1, 0.5), true);
        Assert.NotNull(placed);
        Assert.Equal("double", placed.GetProperty("type"));
        Assert.Equal("false", placed.GetProperty("waterlogged"));

        // A bottom slab's top face accepts the slab, its side accepts it only from above the middle, and a different kind never does.
        Assert.True(VanillaPlacement.SlabCanBeReplaced(bottom, slab, true, BlockFace.Up, false));
        Assert.True(VanillaPlacement.SlabCanBeReplaced(bottom, slab, true, BlockFace.North, true));
        Assert.False(VanillaPlacement.SlabCanBeReplaced(bottom, slab, true, BlockFace.North, false));
        Assert.False(VanillaPlacement.SlabCanBeReplaced(bottom, slab, true, BlockFace.Down, false));
        Assert.False(VanillaPlacement.SlabCanBeReplaced(bottom, stoneSlab, true, BlockFace.Up, false));

        // A top slab's underside accepts it, its side only from below the middle.
        Assert.True(VanillaPlacement.SlabCanBeReplaced(top, slab, true, BlockFace.Down, false));
        Assert.True(VanillaPlacement.SlabCanBeReplaced(top, slab, true, BlockFace.East, false));
        Assert.False(VanillaPlacement.SlabCanBeReplaced(top, slab, true, BlockFace.East, true));

        // Next to an existing slab (not replacing the clicked block), any non-double slab of the same kind fills it.
        Assert.True(VanillaPlacement.SlabCanBeReplaced(bottom, slab, false, BlockFace.North, false));
        Assert.False(VanillaPlacement.SlabCanBeReplaced(slab.WithProperty("type", "double"), slab, false, BlockFace.North, false));
    }
}
