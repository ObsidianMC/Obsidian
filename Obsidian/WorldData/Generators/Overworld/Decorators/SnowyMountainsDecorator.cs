using Obsidian.Utilities.Registry;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class SnowyMountainsDecorator : BaseDecorator
{
    public SnowyMountainsDecorator(Biomes biome, Chunk chunk, Vector surfacePos, BaseBiomeNoise noise) : base(biome, chunk, surfacePos, noise)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < noise.settings.WaterLevel)
        {
            FillWater();
            return;
        }

        var topBlock = Registry.GetBlock(Material.SnowBlock);

        for (int y = 0; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), topBlock);

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        var decorator1 = noise.Decoration(worldX * 0.1, 8, worldZ * 0.1);
        if (decorator1 > 0 && decorator1 < 0.5) // 50% chance for grass
            chunk.SetBlock(pos, Registry.GetBlock(Material.FrostedIce));

        var poppyNoise = noise.Decoration(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            chunk.SetBlock(pos, new Block(Material.Gravel));
    }
}
