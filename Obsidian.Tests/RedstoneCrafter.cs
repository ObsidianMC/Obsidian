using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Xunit;

namespace Obsidian.Tests;

public class RedstoneCrafterTests
{
    private static readonly Vector Crafter = new(8, 64, 8);
    private static readonly Vector Source = new(7, 64, 8);

    [Fact]
    public async Task RisingSignalCraftsFourTicksLater()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        await world.Place(Crafter, BlocksRegistry.GetDefault(BlockIds.Crafter).WithProperty("triggered", "false"));

        await world.Place(Source, BlocksRegistry.Get("minecraft:redstone_block"));
        Assert.Equal("true", world.GetLoadedBlock(Crafter)!.GetProperty("triggered"));
        Assert.Empty(world.CraftRequests);

        for (var tick = 0; tick < 3; tick++)
            await world.Advance();
        Assert.Empty(world.CraftRequests);

        await world.Advance();
        Assert.Equal([Crafter], world.CraftRequests);
    }

    [Fact]
    public async Task RemovingTheSignalUntriggersTheCrafterButTheScheduledCraftStillRuns()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        await world.Place(Crafter, BlocksRegistry.GetDefault(BlockIds.Crafter).WithProperty("triggered", "false"));
        await world.Place(Source, BlocksRegistry.Get("minecraft:redstone_block"));
        await world.Place(Source, BlocksRegistry.Air);

        // Vanilla's CrafterBlock.tick runs dispenseFrom without checking the triggered flag.
        Assert.Equal("false", world.GetLoadedBlock(Crafter)!.GetProperty("triggered"));
        for (var tick = 0; tick < 4; tick++)
            await world.Advance();
        Assert.Equal([Crafter], world.CraftRequests);
    }
}

public class RedstoneDispenserTests
{
    [Theory]
    [InlineData("minecraft:dispenser", false)]
    [InlineData("minecraft:dropper", true)]
    public async Task RisingSignalFiresFourTicksLater(string block, bool dropper)
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15, ringChunks: 1);
        var position = new Vector(8, 64, 8);
        await world.Place(position, BlocksRegistry.Get(block).WithProperty("facing", "north").WithProperty("triggered", "false"));
        await world.Place(new Vector(7, 64, 8), BlocksRegistry.Get("minecraft:redstone_block"));

        for (var tick = 0; tick < 3; tick++)
            await world.Advance();
        Assert.Empty(world.DispenserFires);

        await world.Advance();
        Assert.Equal([(position, dropper)], world.DispenserFires);
    }
}
