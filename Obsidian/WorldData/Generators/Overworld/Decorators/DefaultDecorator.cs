namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class DefaultDecorator : BaseDecorator
{
    public DefaultDecorator(Biome biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
    }

    public override void Decorate()
    {
        if (pos.Y < noise.Settings.WaterLevel)
        {
            FillWater();
            return;
        }
    }
}
