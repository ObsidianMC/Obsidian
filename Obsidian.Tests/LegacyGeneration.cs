using Obsidian.API;
using Obsidian.API.Registries;
using Obsidian.Registries;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.Tests.Fakes;
using Obsidian.WorldData;
using Obsidian.WorldData.Decorators;
using Obsidian.WorldData.Generators;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace Obsidian.Tests;

/// <summary>
/// The legacy overworld generator's decorators.
/// </summary>
public sealed class LegacyGeneration
{
    // A column with no terrain down to the bedrock leaves its surface at the world's bottom, which a long pregeneration
    // eventually meets in a deep ocean. Every decorator layers blocks under its surface, which used to fail the chunk.
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task DecoratorsOnTheWorldsBottomStayInsideIt()
    {
        await using var context = TestServerFactory.CreateServer($"legacy-bottom-{Guid.NewGuid():N}");
        var helper = new GenHelper(context.World);
        var decorators = typeof(BaseDecorator).Assembly.GetTypes()
            .Where(type => type.IsClass && !type.IsAbstract && type.IsSubclassOf(typeof(BaseDecorator)));

        foreach (var type in decorators)
        {
            var chunk = new Chunk(0, 0);
            var bottom = new Vector(3, chunk.MinY, 5);
            var decorator = (BaseDecorator)Activator.CreateInstance(type, CodecRegistry.Biomes.Ocean, chunk, bottom, helper)!;

            decorator.Decorate();
        }
    }

    [Fact]
    public void BlocksOutsideTheBuildHeightArentPlaced()
    {
        var chunk = new Chunk(0, 0);
        var top = chunk.MinY + chunk.Height;

        chunk.SetBlock(0, chunk.MinY - 1, 0, BlocksRegistry.Stone);
        chunk.SetBlock(0, top, 0, BlocksRegistry.Stone);
        chunk.SetBlock(0, top - 1, 0, BlocksRegistry.Stone);

        Assert.True(chunk.GetBlock(0, chunk.MinY, 0).IsAir);
        Assert.True(chunk.GetBlock(0, top - 1, 0).Is(BlockIds.Stone));
    }
}
