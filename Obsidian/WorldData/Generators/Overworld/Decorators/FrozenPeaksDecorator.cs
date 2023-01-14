using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class FrozenPeaksDecorator : BaseDecorator
{
    public FrozenPeaksDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < noise.Settings.WaterLevel)
        {
            FillWater();
            return;
        }

        for (int y = 0; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), BlocksRegistry.SnowBlock);

        int worldX = (chunk.X << 4) + pos.X;
        int worldZ = (chunk.Z << 4) + pos.Z;

        var decorator1 = noise.Decoration.GetValue(worldX * 0.1, 8, worldZ * 0.1);
        if (decorator1 > 0 && decorator1 < 0.5) // 50% chance for grass
            chunk.SetBlock(pos, BlocksRegistry.FrostedIce);

        var poppyNoise = noise.Decoration.GetValue(worldX * 0.03, 9, worldZ * 0.03); // 0.03 makes more groupings
        if (poppyNoise > 1)
            chunk.SetBlock(pos, BlocksRegistry.Gravel);
    }
}
