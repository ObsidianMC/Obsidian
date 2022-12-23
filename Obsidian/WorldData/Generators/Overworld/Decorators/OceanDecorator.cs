using Obsidian.API.BlockStates.Builders;
using Obsidian.Utilities.Registry;

namespace Obsidian.WorldData.Generators.Overworld.Decorators;

public class OceanDecorator : BaseDecorator
{
    protected readonly IBlock bubble, sand, dirt, gravel, clay, magma, seaGrass, tallSeaGrass, kelp;
    protected readonly IBlock tallSeaGrassUpperState = BlocksRegistry.Get(Material.TallSeagrass, new TallSeagrassStateBuilder().WithHalf(EHalf.Upper).Build());

    protected IBlock primarySurface, secondarySurface, tertiarySurface;

    protected bool hasSeaGrass, hasKelp, hasMagma = true;

    protected bool IsSurface2 => noise.Decoration.GetValue(pos.X + (chunk.X * 16) / 12.0, 9, pos.Z + (chunk.Z * 16) / 12.0) > 0.666;

    protected bool isSurface3 => noise.Decoration.GetValue(pos.X + (chunk.X * 16) / 12.0, 90, pos.Z + (chunk.Z * 16) / 12.0) < -0.666;

    protected bool IsGrass => noise.Decoration.GetValue(pos.X + (chunk.X * 16), 900, pos.Z + (chunk.Z * 16)) > 0.4;

    protected bool IsTallGrass => noise.Decoration.GetValue(pos.X + (chunk.X * 16), 900, pos.Z + (chunk.Z * 16)) < -0.4;

    protected bool IsKelp => noise.Decoration.GetValue(pos.X + (chunk.X * 16), -900, pos.Z + (chunk.Z * 16)) > 0.75;

    protected bool IsMagma => noise.Decoration.GetValue(pos.X + (chunk.X * 16) / 2.0, -90, pos.Z + (chunk.Z * 16) / 2.0) > 0.85;

    public OceanDecorator(Biomes biome, Chunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
    {
        sand = BlocksRegistry.Sand;
        dirt = BlocksRegistry.Dirt;
        gravel = BlocksRegistry.Gravel;
        clay = BlocksRegistry.Clay;
        magma = BlocksRegistry.MagmaBlock;
        seaGrass = BlocksRegistry.Seagrass;
        tallSeaGrass = BlocksRegistry.TallSeagrass;
        kelp = BlocksRegistry.KelpPlant;
        bubble = BlocksRegistry.BubbleColumn;

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
            chunk.SetBlock(pos + (0, 1, 0), tallSeaGrass);
            chunk.SetBlock(pos + (0, 2, 0), tallSeaGrassUpperState);
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
