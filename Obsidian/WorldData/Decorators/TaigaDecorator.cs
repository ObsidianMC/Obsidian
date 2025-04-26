using Obsidian.WorldData.Features.Trees;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class TaigaDecorator : BaseDecorator
{
    public TaigaDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(2, typeof(SpruceTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(3, typeof(LargeSpruceTree)));
    }

    public override void Decorate()
    {
        if (Position.Y < Noise.WaterLevel)
        {
            FillWater();
            return;
        }

        int worldX = (Chunk.X << 4) + Position.X;
        int worldZ = (Chunk.Z << 4) + Position.Z;

        Chunk.SetBlock(Position, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Dirt);

        if (!Chunk.GetBlock(Position + (0, 1, 0)).IsAir) { return; }

        var grassNoise = Noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.1)
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.ShortGrass);

        var dandelionNoise = Noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (dandelionNoise > 0 && dandelionNoise < 0.05)
        {
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.Dandelion);
        }

        if (Noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03) > 1)
        {
            Chunk.SetBlock(Position, BlocksRegistry.CoarseDirt);
        }

        if (Noise.Decoration.GetValue(worldX * 0.75, 4, worldZ * 0.75) > 0.95)
        {
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.SweetBerryBush);
        }
    }
}
