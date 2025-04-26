using Obsidian.WorldData.Features.Flora;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class GroveDecorator : BaseDecorator
{
    public GroveDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
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
