using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class StonyPeaksDecorator : BaseDecorator
{
    public StonyPeaksDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
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
            chunk.SetBlock(pos, BlocksRegistry.Gravel);
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        var cobbleNoise = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (cobbleNoise > 0 && cobbleNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos + (0, -1, 0), BlocksRegistry.Cobblestone);

        var mossNoise = noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03);
        if (mossNoise > 1)
            chunk.SetBlock(pos, BlocksRegistry.MossBlock);

        var clayNoise = noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03);
        if (clayNoise > 1)
            chunk.SetBlock(pos + (0, -1, 0), BlocksRegistry.Clay);

        var grassNoise = noise.Decoration.GetValue(worldX * 0.03, 11, worldZ * 0.03);
        if (grassNoise > 1)
            chunk.SetBlock(pos, BlocksRegistry.Grass);

        var coalNoise = noise.Decoration.GetValue(worldX * 0.03, 12, worldZ * 0.03);
        if (coalNoise > 1)
            chunk.SetBlock(pos + (0, -1, 0), BlocksRegistry.CoalOre);

        var ironNoise = noise.Decoration.GetValue(worldX * 0.03, 13, worldZ * 0.03);
        if (ironNoise > 1)
            chunk.SetBlock(pos + (0, -1, 0), BlocksRegistry.IronOre);
    }
}
