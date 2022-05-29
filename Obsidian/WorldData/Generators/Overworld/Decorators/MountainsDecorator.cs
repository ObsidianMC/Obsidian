using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class MountainsDecorator : BaseDecorator
{
    public MountainsDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
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
            chunk.SetBlock(pos, new Block(Material.SnowBlock));
            return;
        }

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        var grassNoise = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (grassNoise > 0 && grassNoise < 0.5) // 50% chance for grass
            chunk.SetBlock(pos, Registry.GetBlock(Material.Cobblestone));

        var poppyNoise = noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            chunk.SetBlock(pos, new Block(Material.Gravel));

        var dandyNoise = noise.Decoration.GetValue(worldX * 0.03, 10, worldZ * 0.03); // 0.03 makes more groupings
        if (dandyNoise > 1)
            chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Clay));

        var cornFlowerNoise = noise.Decoration.GetValue(worldX * 0.03, 11, worldZ * 0.03); // 0.03 makes more groupings
        if (cornFlowerNoise > 1)
            chunk.SetBlock(pos + (0, 1, 0), Registry.GetBlock(Material.Grass));

        var azureNoise = noise.Decoration.GetValue(worldX * 0.03, 12, worldZ * 0.03); // 0.03 makes more groupings
        if (azureNoise > 1)
            chunk.SetBlock(pos, Registry.GetBlock(Material.Snow));
    }
}
