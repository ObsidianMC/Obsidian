using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class StonyPeaksDecorator : BaseDecorator
{
    public StonyPeaksDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < 74)
        {
            chunk.SetBlock(pos, new Block(Material.GrassBlock, 1));
            for (int y = pos.Y - 1; y > pos.Y - 5; y--)
            {
                chunk.SetBlock(pos.X, y, pos.Z, new Block(Material.Dirt));
            }
        }

        if (pos.Y > 120)
        {
            chunk.SetBlock(pos, new Block(Material.Gravel));
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        var cobbleNoise = noise.Decoration(worldX * 0.1, 8, worldZ * 0.1);
        if (cobbleNoise > 0 && cobbleNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos + (0, -1, 0), Registry.GetBlock(Material.Cobblestone));

        var mossNoise = noise.Decoration(worldX * 0.03, 9, worldZ * 0.03);
        if (mossNoise > 1)
            chunk.SetBlock(pos, new Block(Material.MossBlock));

        var clayNoise = noise.Decoration(worldX * 0.03, 10, worldZ * 0.03);
        if (clayNoise > 1)
            chunk.SetBlock(pos + (0, -1, 0), Registry.GetBlock(Material.Clay));

        var grassNoise = noise.Decoration(worldX * 0.03, 11, worldZ * 0.03);
        if (grassNoise > 1)
            chunk.SetBlock(pos, Registry.GetBlock(Material.Grass));

        var coalNoise = noise.Decoration(worldX * 0.03, 12, worldZ * 0.03);
        if (coalNoise > 1)
            chunk.SetBlock(pos + (0, -1, 0), Registry.GetBlock(Material.CoalOre));

        var ironNoise = noise.Decoration(worldX * 0.03, 13, worldZ * 0.03);
        if (ironNoise > 1)
            chunk.SetBlock(pos + (0, -1, 0), Registry.GetBlock(Material.IronOre));
    }
}
