namespace Obsidian.WorldData.Fluids;

/// <summary>
/// Vanilla's <c>WaterFluid</c>: ticks every 5 ticks, flows 7 blocks (drop-off 1), looks 4 blocks for a drop, and forms
/// new sources when the water source conversion rule is on.
/// </summary>
internal sealed class WaterFluid : FlowingFluid
{
    public static WaterFluid Instance { get; } = new();

    private WaterFluid()
    {
    }

    public override FluidKind SourceKind => FluidKind.Water;

    public override FluidKind FlowingKind => FluidKind.FlowingWater;

    public override int GetTickDelay(FluidLevel level) => 5;

    /// <remarks>
    /// Only other fluids can replace water, and only from above.
    /// </remarks>
    public override bool CanBeReplacedWith(FluidState state, FluidLevel level, Vector position, FluidKind fluid, BlockFace direction) =>
        direction == BlockFace.Down && !FluidState.IsWaterKind(fluid);

    protected override int GetSlopeFindDistance(FluidLevel level) => 4;

    protected override int GetDropOff(FluidLevel level) => 1;

    protected override bool CanConvertToSource(FluidLevel level) => level.Rules.WaterSourceConversion;

    /// <remarks>
    /// Vanilla drops the destroyed block's loot here; Obsidian doesn't spawn block drops for fluids yet.
    /// </remarks>
    protected override void BeforeDestroyingBlock(FluidLevel level, Vector position, IBlock block)
    {
    }
}
