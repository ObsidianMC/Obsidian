using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class FrozenRiverDecorator : BaseDecorator
{
    public FrozenRiverDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        var sand = BlocksRegistry.Get(Material.Sand);
        var dirt = BlocksRegistry.Get(Material.Dirt);
        var gravel = BlocksRegistry.Get(Material.Gravel);
        var water = BlocksRegistry.Get(Material.Water);
        var ice = BlocksRegistry.Get(Material.Ice);

        if (pos.Y <= 64)
        {
            chunk.SetBlock(pos, gravel);
            for (int y = 63; y > pos.Y; y--)
            {
                chunk.SetBlock(pos.X, y, pos.Z, water);
            }
            chunk.SetBlock(pos.X, 64, pos.Z, ice);
        }
        else
        {
            chunk.SetBlock(pos, sand);
            for (int y = -1; y > -4; y--)
                chunk.SetBlock(pos + (0, y, 0), sand);
        }
    }
}
