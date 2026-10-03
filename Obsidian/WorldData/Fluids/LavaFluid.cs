namespace Obsidian.WorldData.Fluids;

/// <summary>
/// Vanilla's <c>LavaFluid</c>. Where lava is fast (the nether's <c>fast_lava</c>) it ticks every 10 ticks, flows 7
/// blocks and looks 4 blocks for a drop; elsewhere it ticks every 30 ticks, flows 3 blocks (drop-off 2) and looks 2 blocks.
/// Lava falling onto water turns it to stone.
/// </summary>
internal sealed class LavaFluid : FlowingFluid
{
    // Vanilla LavaFluid.MIN_LEVEL_CUTOFF: lava lower than this can't be replaced by water.
    private const float MinLevelCutoff = 0.44444445f;

    public static LavaFluid Instance { get; } = new();

    private LavaFluid()
    {
    }

    public override FluidKind SourceKind => FluidKind.Lava;

    public override FluidKind FlowingKind => FluidKind.FlowingLava;

    public override int GetTickDelay(FluidLevel level) => level.Rules.FastLava ? 10 : 30;

    public override bool CanBeReplacedWith(FluidState state, FluidLevel level, Vector position, FluidKind fluid, BlockFace direction) =>
        state.GetHeight(level, position) >= MinLevelCutoff && FluidState.IsWaterKind(fluid);

    protected override int GetSlopeFindDistance(FluidLevel level) => level.Rules.FastLava ? 4 : 2;

    protected override int GetDropOff(FluidLevel level) => level.Rules.FastLava ? 1 : 2;

    protected override bool CanConvertToSource(FluidLevel level) => level.Rules.LavaSourceConversion;

    /// <remarks>
    /// Lava rising in a flowing block usually waits four times longer (three times in four, from the level's random).
    /// </remarks>
    protected override int GetSpreadDelay(FluidLevel level, Vector position, FluidState current, FluidState next)
    {
        var delay = this.GetTickDelay(level);
        if (!current.IsEmpty && !next.IsEmpty && !current.Falling && !next.Falling
            && next.GetHeight(level, position) > current.GetHeight(level, position) && level.Random.Next(4) != 0)
        {
            delay *= 4;
        }

        return delay;
    }

    protected override void BeforeDestroyingBlock(FluidLevel level, Vector position, IBlock block) => level.Fizz(position);

    protected override void SpreadTo(FluidLevel level, Vector position, IBlock block, BlockFace direction, FluidState fluid)
    {
        if (direction == BlockFace.Down && level.GetFluid(position).IsWater)
        {
            // Only a water block turns to stone; a waterlogged block just keeps its water.
            if (block.IsLiquidBlock())
                level.SetBlock(position, BlocksRegistry.Stone, BlockUpdateFlags.All);

            level.Fizz(position);
            return;
        }

        base.SpreadTo(level, position, block, direction, fluid);
    }
}
