using Obsidian.API;
using Obsidian.API.Blocks;
using Obsidian.API.Registries;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Plugins;
using Obsidian.Plugins.Archives;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

public sealed class CopperWeatheringInteractions
{
    [Fact]
    public async Task WeatheringPreservesPartnerReplacementFromPluginNeighborHook()
    {
        await using var context = TestServerFactory.CreateServer($"copper-plugin-{Guid.NewGuid():N}");
        await BlockUpdates.AddChunksAsync(context.World);
        await using var owner = new PluginContainer
        {
            Info = new PluginInfo("copperprobe"),
            Archive = PluginArchive.Open("copperprobe.obby", PluginArchives.Zip(new Dictionary<string, byte[]>
            {
                [PluginMetadata.FileName] = PluginArchives.Metadata("copperprobe", "1.0.0", "Copper probe")
            }))
        };
        var left = new Vector(8, 72, 8);
        var right = left + Vector.East;
        var properties = new BlockProperties { Behavior = new ReplacePartner(left, right) };
        var reactor = BlocksRegistry.Register("copperprobe:partner_replacer", properties);
        Assert.True(BlocksRegistry.TryBind(reactor, properties, owner, () => owner.IsClosing));

        try
        {
            // Start with a connected pair and a plugin block beside the half that receives the random tick.
            var world = context.World;
            var chest = BlocksRegistry.ExposedCopperChest.WithProperty("facing", "north");
            var setup = BlockUpdateFlags.KnownShape | BlockUpdateFlags.SkipOnPlace;
            world.BlockUpdates.SetBlock(left, chest.WithProperty("type", "left"), setup);
            world.BlockUpdates.SetBlock(right, chest.WithProperty("type", "right"), setup);
            world.BlockUpdates.SetBlock(left + Vector.West, reactor.DefaultState, setup);

            // This seed ages the pair in the captured vanilla fixture. The plugin replaces the right half
            // synchronously inside the left half's SetBlock, before the weathering routine returns.
            var behavior = BlocksRegistry.GetRandomTickBehavior(chest.RegistryId);
            Assert.NotNull(behavior);
            behavior.RandomTick(world, left, world.BlockUpdates.GetBlock(left), new LegacyRandomSource(4610));

            Assert.Equal(BlockIds.WeatheredCopperChest, world.BlockUpdates.GetBlock(left).RegistryId);
            Assert.True(world.BlockUpdates.GetBlock(right).IsSameState(BlocksRegistry.DiamondBlock));
            Assert.Null(world.GetResidentBlockEntity(right));
        }
        finally
        {
            owner.Close();
            BlocksRegistry.ReleasePlugin(owner);
        }
    }

    private sealed class ReplacePartner(Vector left, Vector right) : IBlockBehavior
    {
        public void NeighborChanged(NeighborChangedContext context)
        {
            if (context.SourcePosition == left
                && context.Level.GetBlock(left).Is(BlockIds.WeatheredCopperChest))
            {
                context.Level.SetBlock(right, BlocksRegistry.DiamondBlock);
            }
        }
    }
}
