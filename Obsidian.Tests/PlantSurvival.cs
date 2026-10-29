using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.WorldData;
using Obsidian.WorldData.Features;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Plants that need a block under them (sugar cane, cactus): their owner classes are generated, and breaking the block
/// under a stack breaks every plant above it, one tick each, as vanilla's canSurvive checks do.
/// </summary>
public sealed class PlantSurvival
{
    private const int Ground = 69;

    private static readonly Vector Base = new(8, Ground + 1, 8);

    [Fact(DisplayName = "Sugar cane and cactus are owned by vanilla's SugarCaneBlock and CactusBlock")]
    public void OwnersAreGenerated()
    {
        Assert.Same(TickBehaviors.SugarCaneBlock, BlocksRegistry.GetTickBehavior(BlockIds.SugarCane));
        Assert.Same(ShapeUpdateBehaviors.SugarCaneBlock, BlocksRegistry.GetShapeUpdateBehavior(BlockIds.SugarCane));
        Assert.Same(TickBehaviors.CactusBlock, BlocksRegistry.GetTickBehavior(BlockIds.Cactus));
        Assert.Same(ShapeUpdateBehaviors.CactusBlock, BlocksRegistry.GetShapeUpdateBehavior(BlockIds.Cactus));
        Assert.Same(TickBehaviors.BambooStalkBlock, BlocksRegistry.GetTickBehavior(BlockIds.Bamboo));
        Assert.Same(ShapeUpdateBehaviors.BambooStalkBlock, BlocksRegistry.GetShapeUpdateBehavior(BlockIds.Bamboo));
    }

    [Theory(DisplayName = "Breaking the bottom of a stack breaks every plant above it", Timeout = TestServerFactory.TestTimeout)]
    [InlineData(BlockIds.SugarCane, 3, true)]
    [InlineData(BlockIds.Bamboo, 3, false)]
    [InlineData(BlockIds.Cactus, 2, false)]
    public async Task BreakingTheBottomBreaksTheStack(int plant, int height, bool needsWater)
    {
        await using var context = TestServerFactory.CreateServer($"plant-stack-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        // Chunks only tick while something simulates them; a chunk ticket keeps this one ticking.
        using var ticket = world.AddChunkTicket(0, 0);

        // Sand (with water beside it for sugar cane): the bottom plant can stand there.
        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.Sand));
        if (needsWater)
            Held(world).SetBlock(Base + Vector.Down + Vector.East, BlocksRegistry.GetDefault(BlockIds.Water));
        for (var y = 0; y < height; y++)
            Held(world).SetBlock(Base + new Vector(0, y, 0), BlocksRegistry.GetDefault(plant));

        Assert.True(world.BlockUpdates.RemoveBlock(Base));
        for (var tick = 0; tick < 3; tick++)
            await world.DoWorldTickAsync();

        for (var y = 0; y < height; y++)
            Assert.True(world.BlockUpdates.GetBlock(Base + new Vector(0, y, 0)).IsAir);
    }

    [Fact(DisplayName = "A sugar cane stalk on sand beside water stays when its neighbours change", Timeout = TestServerFactory.TestTimeout)]
    public async Task SupportedSugarCaneStays()
    {
        await using var context = TestServerFactory.CreateServer($"plant-cane-stay-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.Sand));
        Held(world).SetBlock(Base + Vector.Down + Vector.East, BlocksRegistry.GetDefault(BlockIds.Water));
        Held(world).SetBlock(Base, BlocksRegistry.GetDefault(BlockIds.SugarCane));
        Held(world).SetBlock(Base + Vector.Up, BlocksRegistry.GetDefault(BlockIds.SugarCane));

        // A change beside the top stalk runs its shape update; the stalks are still supported.
        world.BlockUpdates.SetBlock(Base + Vector.East, BlocksRegistry.Air);
        await world.DoWorldTickAsync();

        Assert.Equal(BlockIds.SugarCane, world.BlockUpdates.GetBlock(Base).RegistryId);
        Assert.Equal(BlockIds.SugarCane, world.BlockUpdates.GetBlock(Base + Vector.Up).RegistryId);
    }

    [Fact(DisplayName = "Bamboo placed on grass becomes a sapling, and on bamboo a stalk")]
    public async Task BambooPlacementFollowsVanillaBaseRules()
    {
        await using var context = TestServerFactory.CreateServer($"plant-bamboo-place-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        using var ticket = world.AddChunkTicket(0, 0);

        IBlock Place(Vector at)
        {
            var placed = new BlockPlaceContext
            {
                Level = world.BlockUpdates,
                Player = null!,
                Hand = InteractionHand.MainHand,
                Item = null!,
                Block = BlocksRegistry.GetDefault(BlockIds.Bamboo),
                Position = at,
                ClickedPosition = at + Vector.Down,
                ClickedFace = BlockFace.Up,
                ClickedAt = default,
                ReplacesClicked = false,
                HorizontalDirection = BlockFace.North,
                NearestLookDirection = BlockFace.North,
            };
            return PlacementBehaviors.For(placed.Block)!.GetStateForPlacement(placed)!;
        }

        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.GrassBlock));
        Assert.Equal(BlockIds.BambooSapling, Place(Base).RegistryId);

        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.BambooSapling));
        var onSapling = Place(Base);
        Assert.Equal(BlockIds.Bamboo, onSapling.RegistryId);
        Assert.Equal("none", onSapling.GetProperty("leaves"));

        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.Bamboo).WithProperty("leaves", "large"));
        Assert.Equal("small", Place(Base).GetProperty("leaves"));

        Held(world).SetBlock(Base + Vector.Down, BlocksRegistry.GetDefault(BlockIds.Stone));
        Assert.Null(PlacementBehaviors.For(BlocksRegistry.GetDefault(BlockIds.Bamboo))!
            .GetStateForPlacement(new BlockPlaceContext { Level = world.BlockUpdates, Player = null!, Hand = InteractionHand.MainHand, Item = null!, Block = BlocksRegistry.GetDefault(BlockIds.Bamboo), Position = Base, ClickedPosition = Base, ClickedFace = BlockFace.Up, ClickedAt = default, ReplacesClicked = false, HorizontalDirection = BlockFace.North, NearestLookDirection = BlockFace.North }));
    }

    private static IChunk Held(WorldData.World world) => world.GetLoadedChunk(0, 0)!;

    private static void AddChunks(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new WorldData.Chunk(chunkX, chunkZ);
                for (var x = 0; x < 16; x++)
                {
                    for (var z = 0; z < 16; z++)
                        chunk.SetBlock(chunkX * 16 + x, Ground, chunkZ * 16 + z, BlocksRegistry.GrassBlock);
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
                world.Fluids.Track(chunk);
            }
        }
    }
}
