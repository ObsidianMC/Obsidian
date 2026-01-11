using Obsidian.API.World.Generator;
using Obsidian.API.World.Generator.Noise;
using Obsidian.Registries;

namespace Obsidian.WorldData.Generators.Mojang;

/// <summary>
/// Responsible for applying surface rules to transform stone into surface blocks.
/// Single Responsibility: Surface block placement based on rules.
/// Dependency Inversion: Depends on IBiomeSource abstraction.
/// </summary>
internal class SurfaceBuilder
{
    private readonly NoiseSetting settings;
    private readonly IBiomeSource biomeSource;
    private IBlock defaultBlock => settings.DefaultBlock.ToBlock();

    public SurfaceBuilder(NoiseSetting settings, IBiomeSource biomeSource)
    {
        this.settings = settings;
        this.biomeSource = biomeSource ?? throw new ArgumentNullException(nameof(biomeSource));
    }

    public void ApplySurfaceRules(IChunk chunk)
    {
        if (settings.SurfaceRule == null)
            return;

        int minY = settings.Noise.MinY;
        int maxY = minY + settings.Noise.Height;

        var applier = new SurfaceRuleApplier(
            settings.SurfaceRule,
            settings.NoiseRouter,
            settings.SeaLevel,
            minY,
            maxY);

        int chunkWorldX = chunk.X << 4;
        int chunkWorldZ = chunk.Z << 4;

        for (int x = 0; x < 16; x++)
        {
            for (int z = 0; z < 16; z++)
            {
                ApplySurfaceRulesToColumn(chunk, x, z, chunkWorldX, chunkWorldZ, minY, maxY, applier);
            }
        }
    }

    private void ApplySurfaceRulesToColumn(IChunk chunk, int x, int z, int chunkWorldX, int chunkWorldZ,
        int minY, int maxY, SurfaceRuleApplier applier)
    {
        int worldX = chunkWorldX + x;
        int worldZ = chunkWorldZ + z;

        // Get biome at this column for biome-specific surface rules
        var biome = biomeSource.GetBiome(worldX, 64, worldZ);
        int stoneAboveDepth = 0;
        IBlock? previousBlock = null;

        for (int y = maxY - 1; y >= minY; y--)
        {
            var currentBlock = chunk.GetBlock(x, y, z);

            // Check if current block is air or fluid (not stone)
            bool currentIsAirOrFluid = currentBlock == null || currentBlock.IsAir || IsFluid(currentBlock);
            bool previousIsAirOrFluid = previousBlock == null || previousBlock.IsAir || IsFluid(previousBlock);

            if (currentIsAirOrFluid)
            {
                // Reset depth counter when we hit air/water
                stoneAboveDepth = 0;
            }
            else
            {
                // Current block is solid (stone, dirt, etc.)
                if (previousIsAirOrFluid)
                {
                    stoneAboveDepth = 0; // Reset at surface (first solid block below air/water)
                }

                // ONLY apply surface rules to the default block (stone)
                // This matches Mojang's: if (old == this.defaultBlock)
                if (currentBlock == defaultBlock)
                {
                    var newBlockState = applier.Apply(worldX, y, worldZ, stoneAboveDepth, previousIsAirOrFluid);

                    if (newBlockState != null)
                    {
                        var newBlock = BlocksRegistry.GetFromSimpleState(newBlockState);
                        chunk.SetBlock(x, y, z, newBlock);
                    }
                }

                stoneAboveDepth++;
            }

            previousBlock = currentBlock;
        }
    }

    private static bool IsFluid(IBlock block)
    {
        var name = block.UnlocalizedName;
        return name.Contains("water") || name.Contains("lava");
    }
}
