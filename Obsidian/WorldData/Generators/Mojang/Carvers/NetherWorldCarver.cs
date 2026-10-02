using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Generators.Mojang.Carvers;

/// <summary>
/// The nether's caves: fewer, thicker and taller tunnels that leave lava in their lowest 32 layers, like vanilla's
/// NetherWorldCarver.
/// </summary>
internal sealed class NetherWorldCarver : CaveWorldCarver
{
    protected override int CaveBound => 10;

    protected override double YScale => 5.0;

    protected override float GetThickness(IRandomSource random) => (random.NextFloat() * 2.0f + random.NextFloat()) * 2.0f;

    protected override void CarveBlock(CarvingContext context, CaveCarverConfiguration configuration, int localX, int y, int localZ,
        ref bool surfaceReached)
    {
        if (configuration.Replaceable.Contains(context.Chunk.GetBlock(localX, y, localZ).RegistryId))
            context.Chunk.SetBlock(localX, y, localZ, y <= context.MinY + 31 ? BlocksRegistry.Lava : BlocksRegistry.CaveAir);
    }
}
