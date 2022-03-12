using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class OceanDecorator : BaseDecorator
{
    protected readonly Block bubble, sand, dirt, gravel, clay, magma, seaGrass, tallSeaGrass, kelp;

    protected Block primarySurface, secondarySurface, tertiarySurface;

    protected bool hasSeaGrass, hasKelp, hasMagma = true;

    protected bool IsSurface2 => noise.Decoration.GetValue(pos.X + (chunk.X * 16) / 12.0, 9, pos.Z + (chunk.Z * 16) / 12.0) > 0.666;

    protected bool isSurface3 => noise.Decoration.GetValue(pos.X + (chunk.X * 16) / 12.0, 90, pos.Z + (chunk.Z * 16) / 12.0) < -0.666;

    protected bool IsGrass => noise.Decoration.GetValue(pos.X + (chunk.X * 16), 900, pos.Z + (chunk.Z * 16)) > 0.4;

    protected bool IsTallGrass => noise.Decoration.GetValue(pos.X + (chunk.X * 16), 900, pos.Z + (chunk.Z * 16)) < -0.4;

    protected bool IsKelp => noise.Decoration.GetValue(pos.X + (chunk.X * 16), -900, pos.Z + (chunk.Z * 16)) > 0.75;

    protected bool IsMagma => noise.Decoration.GetValue(pos.X + (chunk.X * 16) / 2.0, -90, pos.Z + (chunk.Z * 16) / 2.0) > 0.85;

    public OceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        sand = Registry.GetBlock(Material.Sand);
        dirt = Registry.GetBlock(Material.Dirt);
        gravel = Registry.GetBlock(Material.Gravel);
        clay = Registry.GetBlock(Material.Clay);
        magma = Registry.GetBlock(Material.MagmaBlock);
        seaGrass = Registry.GetBlock(Material.Seagrass);
        tallSeaGrass = Registry.GetBlock(Material.TallSeagrass);
        kelp = Registry.GetBlock(Material.KelpPlant);
        bubble = new Block(Material.BubbleColumn);

        primarySurface = dirt;
        secondarySurface = sand;
        tertiarySurface = clay;
    }

    public override void Decorate()
    {
        FillWater();

        chunk.SetBlock(pos, IsSurface2 ? secondarySurface : isSurface3 ? tertiarySurface : primarySurface);
        for (int y = -1; y > -4; y--)
            chunk.SetBlock(pos + (0, y, 0), dirt);

        // Add magma
        if (hasMagma & IsMagma)
        {
            chunk.SetBlock(pos, magma);
            for (int y = pos.Y + 1; y <= noise.Settings.WaterLevel; y++)
            {
                chunk.SetBlock(pos.X, y, pos.Z, bubble);
            }
            return;
        }

        // Add sea grass
        if (hasSeaGrass & IsGrass)
        {
            chunk.SetBlock(pos + (0, 1, 0), seaGrass);
        }
        if (hasSeaGrass & IsTallGrass)
        {
            chunk.SetBlock(pos + (0, 1, 0), new Block(Material.TallSeagrass, 1));
            chunk.SetBlock(pos + (0, 2, 0), tallSeaGrass);
        }

        if (hasKelp & IsKelp)
        {
            for (int y = pos.Y + 1; y <= noise.Settings.WaterLevel; y++)
            {
                chunk.SetBlock(pos.X, y, pos.Z, kelp);
            }
        }
    }


}
