using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class SunflowerPlainsDecorator : BaseDecorator
{
    public SunflowerPlainsDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(PoppyFlora), 2, 9));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(OxeyeDaisyFlora), 2, 9));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(FernFlora), 2, 2));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(1, typeof(PumpkinFlora), 5, 1));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(3, typeof(SunflowerFlora), 6, 1));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(7, typeof(SunflowerFlora), 2, 2));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(SunflowerFlora), 8, 2));
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

        var grassNoise = Noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.ShortGrass);
    }
}
