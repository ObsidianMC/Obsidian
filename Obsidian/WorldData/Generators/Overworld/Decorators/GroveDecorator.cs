using Obsidian.Registries;
using Obsidian.WorldData.Generators.Overworld.Features.Flora;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class GroveDecorator : BaseDecorator
{
    public GroveDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(DandelionFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(PoppyFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(AzureBluetFlora), 4, 3));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(4, typeof(TulipFlora), 6, 5));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(4, typeof(OxeyeDaisyFlora), 2, 5));
        Features.Flora.Add(new DecoratorFeatures.FloraInfo(2, typeof(CornflowerFlora), 3, 3));
    }

    public override void Decorate()
    {
        if (pos.Y < noise.Settings.WaterLevel)
        {
            FillSand();
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
