using Obsidian.WorldData.Features.Trees;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class WoodedBadlandsDecorator : BaseDecorator
{
    public WoodedBadlandsDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(2, typeof(OakTree)));
        Features.Trees.Add(new DecoratorFeatures.TreeInfo(7, typeof(DarkOakTree)));
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

        // Flowers
        var grassNoise = Noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.ShortGrass);

        var poppyNoise = Noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            Chunk.SetBlock(Position, BlocksRegistry.CoarseDirt);

        var dandyNoise = Noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
        if (dandyNoise > 1)
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.CobblestoneStairs);

        var cornFlowerNoise = Noise.Decoration.GetValue(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
        if (cornFlowerNoise > 1)
            Chunk.SetBlock(Position + (0, 1, 0), BlocksRegistry.Dirt);

        var azureNoise = Noise.Decoration.GetValue(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
        if (azureNoise > 1)
            Chunk.SetBlock(Position, BlocksRegistry.DarkOakLeaves);
    }
}
