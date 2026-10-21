using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.ChunkData;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Farmland and the hoe, reached through the vanilla classes that own their overrides: <c>FarmBlock</c>'s
/// <c>randomTick</c>, <c>tick</c> and <c>updateShape</c> (generated into <c>BlocksRegistry</c>), and <c>HoeItem.useOn</c>
/// (<c>ItemUseOnBehaviors</c>, from <c>item_use_on_classes.json</c>).
/// </summary>
public sealed class Farmland
{
    private const int Ground = 69;

    private static readonly Vector Farm = new(8, Ground, 8);

    [Fact(DisplayName = "Farmland is owned by FarmBlock for random ticks, ticks and shape updates, and hoes by HoeItem")]
    public void OwnersAreGenerated()
    {
        Assert.Same(RandomTickBehaviors.FarmBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Farmland));
        Assert.Same(TickBehaviors.FarmBlock, BlocksRegistry.GetTickBehavior(BlockIds.Farmland));
        Assert.Same(ShapeUpdateBehaviors.FarmBlock, BlocksRegistry.GetShapeUpdateBehavior(BlockIds.Farmland));
        Assert.Null(BlocksRegistry.GetTickBehavior(BlockIds.Dirt));

        foreach (var hoe in new[] { ItemIds.WoodenHoe, ItemIds.StoneHoe, ItemIds.IronHoe, ItemIds.GoldenHoe, ItemIds.DiamondHoe, ItemIds.NetheriteHoe })
            Assert.NotNull(ItemUseOnBehaviors.For(ItemsRegistry.Get(hoe)));

        Assert.Null(ItemUseOnBehaviors.For(ItemsRegistry.Get(ItemIds.Stick)));
    }

    [Fact(DisplayName = "Farmland within four blocks of water soaks, and dries a step a tick without it")]
    public async Task FarmlandMoistens()
    {
        await using var context = TestServerFactory.CreateServer($"farm-moisture-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        Held(world).SetBlock(Farm, BlocksRegistry.GetDefault(BlockIds.Farmland));
        var random = new LegacyRandomSource(1);

        // Water at the farmland's level, 4 blocks away.
        Held(world).SetBlock(Farm + new Vector(4, 0, 0), BlocksRegistry.Water);
        RandomTickBehaviors.FarmBlock.RandomTick(world, Farm, Held(world).GetBlock(Farm), random);
        Assert.Equal("7", Held(world).GetBlock(Farm).GetProperty("moisture"));

        // Out of reach (5 away): it dries one step a tick.
        Held(world).SetBlock(Farm + new Vector(4, 0, 0), BlocksRegistry.Air);
        Held(world).SetBlock(Farm + new Vector(5, 0, 0), BlocksRegistry.Water);
        for (var moisture = 6; moisture >= 0; moisture--)
        {
            RandomTickBehaviors.FarmBlock.RandomTick(world, Farm, Held(world).GetBlock(Farm), random);
            Assert.Equal(moisture.ToString(), Held(world).GetBlock(Farm).GetProperty("moisture"));
        }
    }

    [Fact(DisplayName = "Dry farmland turns to dirt unless a crop grows on it")]
    public async Task DryFarmlandReverts()
    {
        await using var context = TestServerFactory.CreateServer($"farm-dry-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var random = new LegacyRandomSource(1);

        Held(world).SetBlock(Farm, BlocksRegistry.GetDefault(BlockIds.Farmland));
        Held(world).SetBlock(Farm + Vector.Up, BlocksRegistry.GetDefault(BlockIds.Wheat));
        RandomTickBehaviors.FarmBlock.RandomTick(world, Farm, Held(world).GetBlock(Farm), random);
        Assert.True(Held(world).GetBlock(Farm).Is(BlockIds.Farmland));

        Held(world).SetBlock(Farm + Vector.Up, BlocksRegistry.Air);
        RandomTickBehaviors.FarmBlock.RandomTick(world, Farm, Held(world).GetBlock(Farm), random);
        Assert.True(Held(world).GetBlock(Farm).Is(BlockIds.Dirt));
    }

    [Fact(DisplayName = "A solid block above farmland turns it to dirt on its scheduled tick, a fence gate doesn't")]
    public async Task SolidAboveTurnsToDirt()
    {
        await using var context = TestServerFactory.CreateServer($"farm-solid-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var random = new LegacyRandomSource(1);

        Held(world).SetBlock(Farm, BlocksRegistry.GetDefault(BlockIds.Farmland));
        Held(world).SetBlock(Farm + Vector.Up, BlocksRegistry.GetDefault(BlockIds.OakFenceGate));
        TickBehaviors.FarmBlock.Tick(world.BlockUpdates, Farm, Held(world).GetBlock(Farm), random);
        Assert.True(Held(world).GetBlock(Farm).Is(BlockIds.Farmland));

        Held(world).SetBlock(Farm + Vector.Up, BlocksRegistry.GetDefault(BlockIds.Stone));
        TickBehaviors.FarmBlock.Tick(world.BlockUpdates, Farm, Held(world).GetBlock(Farm), random);
        Assert.True(Held(world).GetBlock(Farm).Is(BlockIds.Dirt));
    }

    [Fact(DisplayName = "A hoe tills grass with air above into farmland and wears, and does nothing under a solid block")]
    public async Task HoeTills()
    {
        await using var context = TestServerFactory.CreateServer($"farm-hoe-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Farmer", new VectorD(8, Ground + 1, 8));
        player.GameMode = GameMode.Survival;
        var hoe = ItemsRegistry.GetSingleItem(ItemsRegistry.Get(ItemIds.DiamondHoe));
        player.Inventory.SetItem(36, hoe);
        var behavior = ItemUseOnBehaviors.For(hoe.Holder)!;

        Assert.True(await behavior.UseOnAsync(world, player, hoe, 36, Farm, Held(world).GetBlock(Farm), BlockFace.Up));
        Assert.True(Held(world).GetBlock(Farm).Is(BlockIds.Farmland));
        Assert.Equal(1, player.Inventory.GetItem(36).Damage);

        // Underside clicks and covered blocks don't till.
        Held(world).SetBlock(Farm, BlocksRegistry.GrassBlock);
        Assert.False(await behavior.UseOnAsync(world, player, hoe, 36, Farm, Held(world).GetBlock(Farm), BlockFace.Down));
        Held(world).SetBlock(Farm + Vector.Up, BlocksRegistry.GetDefault(BlockIds.Stone));
        Assert.False(await behavior.UseOnAsync(world, player, hoe, 36, Farm, Held(world).GetBlock(Farm), BlockFace.Up));
        Assert.True(Held(world).GetBlock(Farm).Is(BlockIds.GrassBlock));
        Assert.Equal(1, player.Inventory.GetItem(36).Damage);

        // Rooted dirt becomes dirt without needing air above, and drops hanging roots.
        Held(world).SetBlock(Farm, BlocksRegistry.GetDefault(BlockIds.RootedDirt));
        Assert.True(await behavior.UseOnAsync(world, player, hoe, 36, Farm, Held(world).GetBlock(Farm), BlockFace.Up));
        Assert.True(Held(world).GetBlock(Farm).Is(BlockIds.Dirt));
    }

    [Fact(DisplayName = "Seeds place the crop vanilla's BlockItem names, and an item named like its block still places it")]
    public void SeedsPlaceTheirCrops()
    {
        Assert.True(BlocksRegistry.TryGetBlock(ItemsRegistry.Get(ItemIds.WheatSeeds), out var wheat));
        Assert.True(wheat!.Is(BlockIds.Wheat));
        Assert.True(BlocksRegistry.TryGetBlock(ItemsRegistry.Get(ItemIds.BeetrootSeeds), out var beetroots));
        Assert.True(beetroots!.Is(BlockIds.Beetroots));
        Assert.True(BlocksRegistry.TryGetBlock(ItemsRegistry.Get(ItemIds.Carrot), out var carrots));
        Assert.True(carrots!.Is(BlockIds.Carrots));
        Assert.True(BlocksRegistry.TryGetBlock(ItemsRegistry.Get(ItemIds.Stone), out var stone));
        Assert.True(stone!.Is(BlockIds.Stone));
        Assert.False(BlocksRegistry.TryGetBlock(ItemsRegistry.Get(ItemIds.Stick), out _));
    }

    [Fact(DisplayName = "A crop is placed only on farmland, so not on top of the crop already growing there")]
    public async Task CropsPlaceOnlyOnFarmland()
    {
        await using var context = TestServerFactory.CreateServer($"farm-place-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        Held(world).SetBlock(Farm, BlocksRegistry.GetDefault(BlockIds.Farmland));
        Held(world).SetBlock(Farm + Vector.Up, BlocksRegistry.GetDefault(BlockIds.Wheat));
        Held(world).SetLightLevel(Farm.X, Farm.Y + 1, Farm.Z, LightType.Sky, 15);
        Held(world).SetLightLevel(Farm.X, Farm.Y + 2, Farm.Z, LightType.Sky, 15);
        var wheat = BlocksRegistry.GetDefault(BlockIds.Wheat);

        Assert.False(PlacementSurvival.CanPlace(world.Fluids, wheat, Farm + Vector.Up * 2));
        Assert.False(PlacementSurvival.CanPlace(world.Fluids, wheat, Farm + Vector.Up * 3));
        Held(world).SetBlock(Farm + Vector.Up, BlocksRegistry.Air);
        Assert.True(PlacementSurvival.CanPlace(world.Fluids, wheat, Farm + Vector.Up));
        Assert.True(PlacementSurvival.CanPlace(world.Fluids, BlocksRegistry.GetDefault(BlockIds.Stone), Farm + Vector.Up * 3));
    }

    [Fact(DisplayName = "Wheat planted on farmland is still there after its chunk unloads and loads again")]
    public async Task PlantedCropSurvivesReload()
    {
        await using var context = TestServerFactory.CreateServer($"farm-reload-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);

        await world.SetBlockAsync(Farm, BlocksRegistry.GetDefault(BlockIds.Farmland));
        await world.SetBlockAsync(Farm + Vector.Up, BlocksRegistry.GetDefault(BlockIds.Wheat));
        await world.DoWorldTickAsync();
        Assert.True(Held(world).GetBlock(Farm + Vector.Up).Is(BlockIds.Wheat));

        var region = world.GetRegionForChunk(0, 0)!;
        await region.UnloadChunk(0, 0);
        var loaded = (await region.GetChunkAsync(0, 0))!;

        Assert.True(loaded.GetBlock(Farm).Is(BlockIds.Farmland));
        Assert.True(loaded.GetBlock(Farm + Vector.Up).Is(BlockIds.Wheat));
    }

    // The level may replace its chunk objects as blocks change, so read the one it holds each time.
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
                    {
                        chunk.SetBlock(chunkX * 16 + x, Ground, chunkZ * 16 + z, BlocksRegistry.GrassBlock);

                        if (x % 4 != 0 || z % 4 != 0)
                            continue;

                        for (var y = -64; y < 320; y += 4)
                            chunk.SetBiome(x, y, z, CodecRegistry.Biomes.Plains);
                    }
                }

                chunk.SetChunkStatus(ChunkGenStage.full);
                world.LoadRegionByChunk(chunkX, chunkZ).SetChunk(chunk);
                world.Fluids.Track(chunk);
            }
        }
    }
}
