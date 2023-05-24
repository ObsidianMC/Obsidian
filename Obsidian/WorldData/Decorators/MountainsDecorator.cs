using Obsidian.Registries;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class MountainsDecorator : BaseDecorator
{
    public MountainsDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < 74)
        {
            chunk.SetBlock(pos, BlocksRegistry.GrassBlock);
            for (int y = pos.Y - 1; y > pos.Y - 5; y--)
            {
                chunk.SetBlock(pos.X, y, pos.Z, BlocksRegistry.Dirt);
            }
        }

        if (pos.Y > 120)
        {
            chunk.SetBlock(pos, BlocksRegistry.SnowBlock);
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos, BlocksRegistry.Cobblestone);

        var poppyNoise = noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            chunk.SetBlock(pos, BlocksRegistry.Gravel);

        var dandyNoise = noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
        if (dandyNoise > 1)
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Clay);

        var cornFlowerNoise = noise.Decoration.GetValue(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
        if (cornFlowerNoise > 1)
            chunk.SetBlock(pos + (0, 1, 0), BlocksRegistry.Grass);

        var azureNoise = noise.Decoration.GetValue(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
        if (azureNoise > 1)
            chunk.SetBlock(pos, BlocksRegistry.Snow);
    }
}
