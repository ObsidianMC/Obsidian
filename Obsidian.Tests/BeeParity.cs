using System;
using System.Threading.Tasks;
using Obsidian.API;
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
    [InlineData(Material.Wheat, 6, Material.Wheat, 7)]
    [InlineData(Material.Carrots, 6, Material.Carrots, 7)]
    [InlineData(Material.Potatoes, 6, Material.Potatoes, 7)]
    [InlineData(Material.Beetroots, 2, Material.Beetroots, 3)]
    [InlineData(Material.MelonStem, 6, Material.MelonStem, 7)]
    [InlineData(Material.PumpkinStem, 6, Material.PumpkinStem, 7)]
    [InlineData(Material.SweetBerryBush, 2, Material.SweetBerryBush, 3)]
    [InlineData(Material.TorchflowerCrop, 1, Material.Torchflower, -1)]
    public async Task NectarGrowsVanillaCropsEvenWithoutMobGriefing(Material material, int age, Material expected, int expectedAge)
    {
        await using var context = TestServerFactory.CreateServer($"bee-crops-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        context.World.LevelData.GameRules["mob_griefing"] = "false";
        chunk.SetBlock(8, 69, 8, BlocksRegistry.Get(material).WithProperty("age", age));
        var bee = new Bee { Level = context.World, Position = new VectorD(8.5, 70, 8.5), HasNectar = true };
        bee.InitializeAi(false);

        await bee.GrowCropsAsync();

        var grown = chunk.GetBlock(8, 69, 8);
        Assert.Equal(expected, grown.Material);
        if (expectedAge >= 0) Assert.Equal(expectedAge.ToString(), grown.GetProperty("age"));
        Assert.Equal(1, bee.CropsGrownSincePollination);
    }

    [Theory]
    [InlineData(Material.CaveVines)]
    [InlineData(Material.CaveVinesPlant)]
    public async Task NectarAddsBerriesOnlyOnce(Material material)
    {
        await using var context = TestServerFactory.CreateServer($"bee-vines-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        chunk.SetBlock(8, 69, 8, BlocksRegistry.Get(material).WithProperty("berries", false));
        var bee = new Bee { Level = context.World, Position = new VectorD(8.5, 70, 8.5), HasNectar = true };
        bee.InitializeAi(false);

        await bee.GrowCropsAsync();
        await bee.GrowCropsAsync();

        Assert.Equal("true", chunk.GetBlock(8, 69, 8).GetProperty("berries"));
        Assert.Equal(1, bee.CropsGrownSincePollination);
    }

    [Fact]
    public async Task BothDepthsGrowAndCounterPersistsUntilNectarIsDelivered()
    {
        await using var context = TestServerFactory.CreateServer($"bee-counter-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        // The carrots sit on farmland; the wheat above them has no farmland under it, so once the carrots grow it loses its
        // support to the shape update, as in vanilla. The counter still counts both growths.
        chunk.SetBlock(8, 67, 8, BlocksRegistry.Get(Material.Farmland).WithProperty("moisture", 7));
        chunk.SetBlock(8, 69, 8, BlocksRegistry.Get(Material.Wheat).WithProperty("age", 0));
        chunk.SetBlock(8, 68, 8, BlocksRegistry.Get(Material.Carrots).WithProperty("age", 0));
        var bee = new Bee { Level = context.World, Position = new VectorD(8.5, 70, 8.5), HasNectar = true };
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

    [Fact]
    public async Task MatureAndUnsupportedCropsDoNotConsumeGrowth()
    {
        await using var context = TestServerFactory.CreateServer($"bee-mature-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        chunk.SetBlock(8, 69, 8, BlocksRegistry.Get(Material.Wheat).WithProperty("age", 7));
        chunk.SetBlock(8, 68, 8, BlocksRegistry.Get(Material.PitcherCrop).WithProperty("age", 0));
        var bee = new Bee { Level = context.World, Position = new VectorD(8.5, 70, 8.5), HasNectar = true };
        bee.InitializeAi(false);

        await bee.GrowCropsAsync();

        Assert.Equal(0, bee.CropsGrownSincePollination);
        Assert.Equal("0", chunk.GetBlock(8, 68, 8).GetProperty("age"));
        Assert.False(new BeeGrowCropGoal(bee).CanUse());
    }

    [Fact]
    public async Task GrowthRequiresNectarAndAHiveWithinFortyEightBlocks()
    {
        await using var context = TestServerFactory.CreateServer($"bee-hive-{Guid.NewGuid():N}");
        var chunk = AddChunk(context.World);
        var hive = new Vector(8, 70, 8);
        chunk.SetBlock(8, 70, 8, BlocksRegistry.Get(Material.Beehive));
        chunk.SetBlockEntity(8, 70, 8, new DataBlockEntity { Id = "minecraft:beehive", BlockPosition = hive });
        var bee = new Bee { Level = context.World, Position = new VectorD(8.5, 70, 9.5), HivePosition = hive };
        bee.InitializeAi(false);

        Assert.True(bee.HasValidHive);
        Assert.False(new BeeGrowCropGoal(bee).CanUse());
        bee.HasNectar = true;
        bee.AngerTicks = 20;
        Assert.False(new BeeGrowCropGoal(bee).CanUse());
        bee.Position = new VectorD(56, 70, 8);
        Assert.False(bee.HasValidHive);
    }

    private static Chunk AddChunk(World world)
    {
        var chunk = new Chunk(0, 0);
        chunk.SetChunkStatus(ChunkGenStage.full);
        world.LoadRegionByChunk(0, 0).SetChunk(chunk);
        return chunk;
    }
}
