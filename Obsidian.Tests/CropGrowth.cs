using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.API.Registries;
using Obsidian.API.Utilities;
using Obsidian.API.World;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Registries;
using Obsidian.ChunkData;
using Obsidian.Tests.Fakes;
using Obsidian.Utilities;
using Obsidian.WorldData;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// Random ticks grow crops like vanilla's <c>CropBlock.randomTick</c>, reached through the vanilla class that owns each
/// crop's override (generated into <c>BlocksRegistry.GetRandomTickBehavior</c>). A crop in light of 9 or more draws its
/// chance once a tick, from the speed set by the farmland under and around it; a crop in less light, or at its last age,
/// draws nothing.
/// </summary>
/// <remarks>
/// The expected sequences below apply vanilla's formulas to a second random with the same seed, so they check the draw
/// order and the chance, not a fixed-seed vanilla probe.
/// </remarks>
public sealed class CropGrowth
{
    private const int Ground = 69;

    private const int Seed = 0x5eed;

    private static readonly Vector Crop = new(8, Ground + 1, 8);

    [Fact(DisplayName = "Crops are random-tick owned by their vanilla classes, and other blocks aren't")]
    public void CropsHaveGeneratedOwners()
    {
        Assert.Same(RandomTickBehaviors.CropBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Wheat));
        Assert.Same(RandomTickBehaviors.CropBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Carrots));
        Assert.Same(RandomTickBehaviors.CropBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Potatoes));
        Assert.Same(RandomTickBehaviors.BeetrootBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.Beetroots));
        Assert.Same(RandomTickBehaviors.TorchflowerCropBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.TorchflowerCrop));

        // Logs have owners without a counterpart yet; saplings are owned by SaplingBlock (see SaplingOwners).
        Assert.Null(BlocksRegistry.GetRandomTickBehavior(BlockIds.OakLog));
        Assert.Same(RandomTickBehaviors.SaplingBlock, BlocksRegistry.GetRandomTickBehavior(BlockIds.OakSapling));
    }

    [Fact(DisplayName = "A crop in light draws its chance once a tick, and grows a step when it comes up 0")]
    public async Task CropGrowsOnItsDraws()
    {
        await using var context = TestServerFactory.CreateServer($"crop-draws-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        Lit(world, Crop, 15);
        world.GetLoadedChunk(0, 0)!.SetBlock(Crop, BlocksRegistry.GetDefault(BlockIds.Wheat));

        // Grass below and nothing around: vanilla's speed is 1, so the chance is nextInt(26) == 0.
        AssertDrawsLikeVanilla(world, Crop, speedBound: 26);
    }

    [Fact(DisplayName = "Farmland under and around a crop speeds its growth, moist more than dry")]
    public async Task FarmlandSpeedsGrowth()
    {
        await using var context = TestServerFactory.CreateServer($"crop-farmland-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        Lit(world, Crop, 15);
        var chunk = world.GetLoadedChunk(0, 0)!;

        // Dry farmland under the crop's 3x3: 1 + 1 + 8 * 1/4 = 4, so nextInt(floor(25 / 4) + 1) = nextInt(7).
        Farm(chunk, moisture: 0);
        chunk.SetBlock(Crop, BlocksRegistry.GetDefault(BlockIds.Wheat));
        AssertDrawsLikeVanilla(world, Crop, speedBound: 7);

        // Moist: 1 + 3 + 8 * 3/4 = 10, so nextInt(floor(25 / 10) + 1) = nextInt(3).
        Farm(chunk, moisture: 7);
        chunk.SetBlock(Crop, BlocksRegistry.GetDefault(BlockIds.Wheat));
        AssertDrawsLikeVanilla(world, Crop, speedBound: 3);
    }

    [Fact(DisplayName = "A crop in light below 9 draws nothing, and one at its last age draws nothing")]
    public async Task CropDrawsNothingInDimLightOrAtLastAge()
    {
        await using var context = TestServerFactory.CreateServer($"crop-dim-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var chunk = world.GetLoadedChunk(0, 0)!;

        Lit(world, Crop, 8);
        chunk.SetBlock(Crop, BlocksRegistry.GetDefault(BlockIds.Wheat));

        var random = new LegacyRandomSource(Seed);
        var untouched = new LegacyRandomSource(Seed);
        for (var tick = 0; tick < 500; tick++)
            RandomTickBehaviors.CropBlock.RandomTick(world, Crop, chunk.GetBlock(Crop), random);

        Assert.Equal("0", chunk.GetBlock(Crop).GetProperty("age"));
        Assert.Equal(untouched.NextInt(), random.NextInt());

        Lit(world, Crop, 15);
        chunk.SetBlock(Crop, BlocksRegistry.GetDefault(BlockIds.Wheat).WithProperty("age", 7));
        RandomTickBehaviors.CropBlock.RandomTick(world, Crop, chunk.GetBlock(Crop), random);
        Assert.Equal("7", chunk.GetBlock(Crop).GetProperty("age"));
        Assert.Equal(untouched.NextInt(), random.NextInt());
    }

    [Fact(DisplayName = "A random tick in a loaded section grows the wheat it reaches and shows the client")]
    public async Task RandomTicksGrowWheatAndShowIt()
    {
        await using var context = TestServerFactory.CreateServer($"crop-flow-{Guid.NewGuid():N}");
        var world = context.World;
        AddChunks(world);
        var chunk = world.GetLoadedChunk(0, 0)!;

        // Every block of the section is wheat in full light, so every random tick reaches a crop.
        for (var x = 0; x < 16; x++)
        {
            for (var y = 64; y < 80; y++)
            {
                for (var z = 0; z < 16; z++)
                {
                    chunk.SetBlock(x, y, z, BlocksRegistry.GetDefault(BlockIds.Wheat));
                }
            }
        }

        // Light after the blocks, since block writes update the light around them.
        for (var x = 0; x < 16; x++)
        {
            for (var y = 64; y < 80; y++)
            {
                for (var z = 0; z < 16; z++)
                    chunk.SetLightLevel(x, y, z, LightType.Sky, 15);
            }
        }

        world.GrowthRandom = new LegacyRandomSource(Seed);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, world, "Grower", new VectorD(8, Ground + 1, 8));
        player.GameMode = GameMode.Survival;
        Assert.True(world.TryAddPlayer(player));

        // The chunks the player has been sent, so that block changes in them reach the client.
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
                player.LoadedChunks.Add(NumericsHelper.IntsToLong(chunkX, chunkZ));
        }
        var client = (TestClient)player.Client;
        client.ClearPackets();

        for (var tick = 0; tick < 1000; tick++)
            world.TickRandomBlocks();

        // The level may have replaced the chunk object while ticking, so read the chunk the level holds now.
        chunk = world.GetLoadedChunk(0, 0)!;

        // Each growth went out through the block-change path as the block the chunk holds afterwards.
        var updates = client.SentPackets.OfType<BlockUpdatePacket>().ToList();
        Assert.NotEmpty(updates);
        Assert.All(updates, update => Assert.True(chunk.GetBlock(update.Position).Is(BlockIds.Wheat)));
        Assert.Contains(updates, update => update.BlockId == chunk.GetBlock(update.Position).GetHashCode());
    }

    // Runs the owner's random tick on a crop, and checks it draws exactly as vanilla does: the same seed, one draw of
    // nextInt(speedBound) (and the crop's own, when it's a torchflower or beetroot, not tested here) per tick in light,
    // growing a step when it comes up 0.
    private static void AssertDrawsLikeVanilla(WorldData.World world, Vector position, int speedBound)
    {
        var chunk = world.GetLoadedChunk(position.X >> 4, position.Z >> 4)!;
        var random = new LegacyRandomSource(Seed);
        var expected = new LegacyRandomSource(Seed);
        var age = int.Parse(chunk.GetBlock(position).GetProperty("age")!);

        for (var tick = 0; tick < 2000 && age < 7; tick++)
        {
            RandomTickBehaviors.CropBlock.RandomTick(world, position, chunk.GetBlock(position), random);

            if (expected.NextInt(speedBound) == 0)
                age++;

            Assert.Equal(age, int.Parse(chunk.GetBlock(position).GetProperty("age")!));
        }

        Assert.Equal(7, age);
        Assert.Equal(expected.NextInt(), random.NextInt());
    }

    private static void Lit(WorldData.World world, Vector position, int light)
    {
        var chunk = world.GetLoadedChunk(position.X >> 4, position.Z >> 4)!;
        chunk.SetLightLevel(position.X, position.Y, position.Z, LightType.Sky, light);
    }

    private static void Farm(IChunk chunk, int moisture)
    {
        for (var dx = -1; dx <= 1; dx++)
        {
            for (var dz = -1; dz <= 1; dz++)
                chunk.SetBlock(Crop.X + dx, Ground, Crop.Z + dz, BlocksRegistry.GetDefault(BlockIds.Farmland).WithProperty("moisture", moisture));
        }
    }

    private static void AddChunks(WorldData.World world)
    {
        for (var chunkX = -1; chunkX <= 1; chunkX++)
        {
            for (var chunkZ = -1; chunkZ <= 1; chunkZ++)
            {
                var chunk = new Chunk(chunkX, chunkZ);

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
            }
        }
    }
}
