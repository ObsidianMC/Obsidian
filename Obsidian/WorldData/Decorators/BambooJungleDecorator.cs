using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class BambooJungleDecorator : BaseDecorator
{
    public BambooJungleDecorator(BiomeCodec biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {

        Features.Trees.Add(new DecoratorFeatures.TreeInfo(4, Registries.ConfiguredFeatures.Trees.MinecraftJungleTree));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(7, Registries.ConfiguredFeatures.Trees.MinecraftMegaJungleTree));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(16, typeof(LargeFernFlora), 6, 4));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(16, typeof(FernFlora), 6, 4));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(1, typeof(MelonFlora), 6, 5));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(20, typeof(JungleBushFlora), 3, 5));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(16, typeof(BambooFlora), 6, 4));
    }

    public override void Decorate()
    {
        if (Position.Y < Noise.WaterLevel)
        {
            FillWater();
            return;
        }

        Chunk.SetBlock(Position, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Dirt);

    }
}
