using Obsidian.Registries;
using Obsidian.WorldData.Generators.Overworld.Features.Flora;
using Obsidian.WorldData.Generators.Overworld.Features.Trees;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class SunflowerPlainsDecorator : BaseDecorator
{
    public SunflowerPlainsDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
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
        if (pos.Y < noise.Settings.WaterLevel)
        {
            FillWater();
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        chunk.SetBlock(pos, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Dirt);

        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Grass);
    }
}
