using Obsidian.Registries;
using Obsidian.WorldData.Generators;
using Obsidian.WorldData.Features.Trees;

namespace Obsidian.WorldData.Decorators;

public class SavannaDecorator : BaseDecorator
{
    public SavannaDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(5, typeof(AcaciaTree)));
    }

    public override void Decorate()
    {
        if (pos.Y < noise.WaterLevel)
        {
            FillWater();
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        chunk.SetBlock(pos, BlocksRegistry.GrassBlock);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.Dirt);

        // Flowers
        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.ShortGrass);

        var poppyNoise = noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            chunk.SetBlock(pos, BlocksRegistry.CoarseDirt);

        var dandyNoise = noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
        if (dandyNoise > 1)
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Sand);

        var cornFlowerNoise = noise.Decoration.GetValue(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
        if (cornFlowerNoise > 1)
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.DeadBush);

        var azureNoise = noise.Decoration.GetValue(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
        if (azureNoise > 1)
            chunk.SetBlock(pos, BlocksRegistry.Lava);
    }
}
