using Obsidian.API.BlockStates.Builders;
using Obsidian.WorldData.Generators;

namespace Obsidian.WorldData.Decorators;

public class OceanDecorator : BaseDecorator
{
    protected readonly IBlock bubble, sand, dirt, gravel, clay, magma, seaGrass, tallSeaGrass, kelp;
    protected readonly IBlock tallSeaGrassUpperState = BlocksRegistry.Get(Material.TallSeagrass, new TallSeagrassStateBuilder().WithHalf(BlockHalf.Upper).Build());

    protected IBlock primarySurface, secondarySurface, tertiarySurface;

    protected bool hasSeaGrass, hasKelp, hasMagma = true;

    protected bool IsSurface2 => Noise.Decoration.GetValue(Position.X + (Chunk.X * 16) / 12.0, 9, Position.Z + (Chunk.Z * 16) / 12.0) > 0.666;

    protected bool isSurface3 => Noise.Decoration.GetValue(Position.X + (Chunk.X * 16) / 12.0, 90, Position.Z + (Chunk.Z * 16) / 12.0) < -0.666;

    protected bool IsGrass => Noise.Decoration.GetValue(Position.X + (Chunk.X * 16), 900, Position.Z + (Chunk.Z * 16)) > 0.4;

    protected bool IsTallGrass => Noise.Decoration.GetValue(Position.X + (Chunk.X * 16), 900, Position.Z + (Chunk.Z * 16)) < -0.4;

    protected bool IsKelp => Noise.Decoration.GetValue(Position.X + (Chunk.X * 16), -900, Position.Z + (Chunk.Z * 16)) > 0.75;

    protected bool IsMagma => Noise.Decoration.GetValue(Position.X + (Chunk.X * 16) / 2.0, -90, Position.Z + (Chunk.Z * 16) / 2.0) > 0.85;

    public OceanDecorator(Biome biome, IChunk chunk, Vector surfacePos, GenHelper helper) : base(biome, chunk, surfacePos, helper)
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

        Chunk.SetBlock(Position, IsSurface2 ? secondarySurface : isSurface3 ? tertiarySurface : primarySurface);
        for (int y = -1; y > -4; y--)
            Chunk.SetBlock(Position + (0, y, 0), dirt);

        // Add magma
        if (hasMagma & IsMagma)
        {
            Chunk.SetBlock(Position, magma);
            for (int y = Position.Y + 1; y <= Noise.WaterLevel; y++)
            {
                Chunk.SetBlock(Position.X, y, Position.Z, bubble);
            }
            return;
        }

        // Add sea grass
        if (hasSeaGrass & IsGrass)
        {
            Chunk.SetBlock(Position + (0, 1, 0), seaGrass);
        }
        if (hasSeaGrass & IsTallGrass)
        {
            Chunk.SetBlock(Position + (0, 1, 0), tallSeaGrass);
            Chunk.SetBlock(Position + (0, 2, 0), tallSeaGrassUpperState);
        }

        if (hasKelp & IsKelp)
        {
            for (int y = Position.Y + 1; y <= Noise.WaterLevel; y++)
            {
                Chunk.SetBlock(Position.X, y, Position.Z, kelp);
            }
        }
    }


}
