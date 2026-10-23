using System;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.API.ChunkData;
using Obsidian.Entities;
using Obsidian.Entities.Factories;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Xunit;

namespace Obsidian.Tests;

public sealed class BeeParity
{
    [Theory]
    [InlineData(BlockIds.Wheat, 6, BlockIds.Wheat, 7)]
    [InlineData(BlockIds.TorchflowerCrop, 1, BlockIds.Torchflower, -1)]
    public async Task NectarGrowsVanillaCropsEvenWithoutMobGriefing(
        int material, int age, int expected, int expectedAge)
    {
        await using var context = TestServerFactory.CreateServer($"bee-crops-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        context.World.LevelData.GameRules["mob_griefing"] = "false";
        chunk.SetBlock(8, 69, 8, BlocksRegistry.GetDefault(material).WithProperty("age", age));
        var bee = new Bee
        {
            Level = context.World,
            Position = new VectorD(8.5, 70, 8.5),
            HasNectar = true
        };
        bee.InitializeAi(false);

        await bee.GrowCropsAsync();

        var grown = chunk.GetBlock(8, 69, 8);
        Assert.Equal(expected, grown.RegistryId);
        if (expectedAge >= 0)
            Assert.Equal(expectedAge.ToString(), grown.GetProperty("age"));
        Assert.Equal(1, bee.CropsGrownSincePollination);
    }

    [Fact]
    public async Task BothDepthsGrowAndCounterPersistsUntilNectarIsDelivered()
    {
        await using var context = TestServerFactory.CreateServer($"bee-counter-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        chunk.SetBlock(8, 69, 8, BlocksRegistry.Wheat.WithProperty("age", 0));
        chunk.SetBlock(8, 68, 8, BlocksRegistry.Carrots.WithProperty("age", 0));
        var bee = new Bee
        {
            Level = context.World,
            Position = new VectorD(8.5, 70, 8.5),
            HasNectar = true
        };
        bee.InitializeAi(false);
        var saved = EntityNbt.Save(bee)!;
        saved.Set(new NbtTag<int>("CropsGrownSincePollination", 9));
        bee = Assert.IsType<Bee>(EntityNbt.Load(saved, context.World));

        await bee.GrowCropsAsync();

        Assert.Equal(11, bee.CropsGrownSincePollination);
        Assert.False(new BeeGrowCropGoal(bee).CanUse());
        bee = Assert.IsType<Bee>(EntityNbt.Load(EntityNbt.Save(bee)!, context.World));
        Assert.Equal(11, bee.CropsGrownSincePollination);
        bee.HasNectar = false;
        Assert.Equal(0, bee.CropsGrownSincePollination);
    }

    private static Chunk AddChunk(World world)
    {
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);
        return chunk;
    }
}
