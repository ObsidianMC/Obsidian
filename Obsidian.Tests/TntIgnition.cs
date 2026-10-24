using Obsidian.API.Registries;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.Registries;
using Obsidian.WorldData.Redstone;
using Xunit;

namespace Obsidian.Tests;

public class TntIgnitionTests
{
    private static readonly Vector Tnt = new(9, 64, 8);
    private static readonly Vector Source = new(8, 64, 8);

    private static IBlock RedstoneBlock => BlocksRegistry.GetDefault(BlockIds.RedstoneBlock);

    private static IBlock TntBlock => BlocksRegistry.GetDefault(BlockIds.Tnt);

    [Fact]
    public async Task PlacingTntBesidePowerPrimesItAndClearsTheBlock()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        await world.Place(Source, RedstoneBlock);

        await world.Place(Tnt, TntBlock);

        Assert.Equal(BlocksRegistry.Air.RegistryId, world.GetLoadedBlock(Tnt)!.RegistryId);
        Assert.Equal([Tnt], world.PrimedTnt);
    }

    [Fact]
    public async Task PowerArrivingAtPlacedTntPrimesIt()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);
        await world.Place(Tnt, TntBlock);
        Assert.Empty(world.PrimedTnt);

        await world.Place(Source, RedstoneBlock);

        Assert.Equal(BlocksRegistry.Air.RegistryId, world.GetLoadedBlock(Tnt)!.RegistryId);
        Assert.Equal([Tnt], world.PrimedTnt);
    }

    [Fact]
    public async Task UnpoweredTntStaysPut()
    {
        var world = new RedstoneTestWorld();
        world.Load(0, 15, 62, 66, 0, 15);

        await world.Place(Tnt, TntBlock);

        Assert.Equal(BlockIds.Tnt, world.GetLoadedBlock(Tnt)!.RegistryId);
        Assert.Empty(world.PrimedTnt);
    }

    [Fact]
    public async Task TntRuleOffKeepsPoweredTntInPlace()
    {
        var world = new RedstoneTestWorld { TntExplodes = false };
        world.Load(0, 15, 62, 66, 0, 15);
        await world.Place(Source, RedstoneBlock);

        await world.Place(Tnt, TntBlock);

        Assert.Equal(BlockIds.Tnt, world.GetLoadedBlock(Tnt)!.RegistryId);
        Assert.Empty(world.PrimedTnt);
    }
}
