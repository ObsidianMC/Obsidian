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

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    public async Task TntPrimesOnlyWhenPoweredAndTheRuleAllowsIt(bool sourceFirst, bool powered, bool tntExplodes)
    {
        var world = new RedstoneTestWorld { TntExplodes = tntExplodes };
        world.Load(0, 15, 62, 66, 0, 15);
        if (sourceFirst)
            await world.Place(Source, RedstoneBlock);

        await world.Place(Tnt, TntBlock);
        if (!sourceFirst)
        {
            Assert.Equal(BlockIds.Tnt, world.GetLoadedBlock(Tnt)!.RegistryId);
            Assert.Empty(world.PrimedTnt);
            if (powered)
                await world.Place(Source, RedstoneBlock);
        }

        if (powered && tntExplodes)
        {
            Assert.Equal(BlocksRegistry.Air.RegistryId, world.GetLoadedBlock(Tnt)!.RegistryId);
            Assert.Equal([Tnt], world.PrimedTnt);
        }
        else
        {
            Assert.Equal(BlockIds.Tnt, world.GetLoadedBlock(Tnt)!.RegistryId);
            Assert.Empty(world.PrimedTnt);
        }
    }
}
