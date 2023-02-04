using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class DesertDecorator : BaseDecorator
{
    public DesertDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
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

        var sand = Registry.GetBlock(Material.Sand);
        var sandstone = Registry.GetBlock(Material.Sandstone);
        var deadbush = Registry.GetBlock(Material.DeadBush);
        var cactus = Registry.GetBlock(Material.Cactus);

        for (int y = 0; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), sand);
        for (int y = -4; y > -7; y--)
            chunk.SetBlock(pos + (0, y, 0), sandstone);

        var bushNoise = noise.Decoration.GetValue(worldX * 0.1, 0, worldZ * 0.1);
        if (bushNoise > 0 && bushNoise < 0.05) // 5% chance for bush
            chunk.SetBlock(pos + (0, 1, 0), deadbush);

        var cactusNoise = noise.Decoration.GetValue(worldX * 0.1, 1, worldZ * 0.1);
        if (cactusNoise > 0 && cactusNoise < 0.01) // 1% chance for cactus
        {
            chunk.SetBlock(pos + (0, 1, 0), cactus);
            chunk.SetBlock(pos + (0, 2, 0), cactus);
            chunk.SetBlock(pos + (0, 3, 0), cactus);
        }
    }
}
