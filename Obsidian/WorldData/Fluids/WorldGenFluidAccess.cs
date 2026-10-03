using Obsidian.WorldData.Generators.Mojang.Features;

namespace Obsidian.WorldData.Fluids;

/// <summary>
/// The chunks around a chunk being post-processed, as fluids see them: like vanilla's <c>postProcessGeneration</c> on a
/// live level, ticks are scheduled with their real delays (in the tick lists of the chunks, which keep them until they tick).
/// </summary>
internal sealed class WorldGenFluidAccess(WorldGenRegion region, FluidRules rules, Random random) : IFluidLevelAccess
{
    public int MinY => region.MinY;

    public int Height => region.Height;

    public FluidRules Rules => rules;

    public Random Random => random;

    public IBlock GetBlock(Vector position) => region.GetBlock(position);

    public bool SetBlock(Vector position, IBlock block) => region.SetBlock(position, block);

    public void ScheduleFluidTick(Vector position, FluidKind fluid, int delay) => region.ScheduleFluidTick(position, fluid, delay);
}
