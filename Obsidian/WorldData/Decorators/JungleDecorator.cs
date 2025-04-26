using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Features.Trees;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class JungleDecorator : BaseDecorator
{
    public JungleDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(1, typeof(OakTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(4, typeof(JungleTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(7, typeof(LargeJungleTree)));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(16, typeof(LargeFernFlora), 6, 4));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(16, typeof(FernFlora), 6, 4));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(1, typeof(MelonFlora), 6, 5));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(20, typeof(JungleBushFlora), 3, 5));
    }

    public override void Decorate()
    {
        if (Position.Y < Noise.WaterLevel)
        {
            FillWater();
            return;
        }

        //int worldX = (chunk.X << 4) + pos.X;
        //int worldZ = (chunk.Z << 4) + pos.Z;

        Chunk.SetBlock(Position, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Dirt);

    }
}
