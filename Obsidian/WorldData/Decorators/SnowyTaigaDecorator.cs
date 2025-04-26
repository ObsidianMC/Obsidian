using Obsidian.WorldData.Features.Trees;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class SnowyTaigaDecorator : BaseDecorator
{
    public SnowyTaigaDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(3, typeof(SpruceTree)));
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

        Chunk.SetBlock(Position, BlocksRegistry.Snow);
        for (int y = -1; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.Dirt);

        for (int y = 0; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.SnowBlock);
        for (int y = -4; y > -7; y--)
            Chunk.SetBlock(Position + (0, y, 0), BlocksRegistry.PackedIce);

        var bushNoise = Noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (bushNoise > 0 && bushNoise < 0.05) // 5% chance for bush
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.Snow);

        var cactusNoise = Noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (cactusNoise > 0 && cactusNoise < 0.01) // 1% chance for cactus
        {
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.FrostedIce);
        }
    }
}
