using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class RiverDecorator : BaseDecorator
{

    public RiverDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        var sand = BlocksRegistry.Get(Material.Sand);
        var dirt = BlocksRegistry.Get(Material.Dirt);
        var gravel = BlocksRegistry.Get(Material.Gravel);

        FillWater();

        if (pos.Y <= noise.Settings.WaterLevel)
        {
            chunk.SetBlock(pos, gravel);
        }
        else
        {
            chunk.SetBlock(pos, sand);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), gravel);
        }
    }
}
