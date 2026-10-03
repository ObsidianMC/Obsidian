namespace Obsidian.WorldData.Fluids;

/// <summary>
/// A block's fluid, like vanilla's <c>FluidState</c>: the fluid (source or flowing water or lava), its amount (8 for
/// sources and falling fluids, down to 1 for the thinnest flow) and whether it's falling.
/// </summary>
/// <remarks>
/// Equal values are the same vanilla fluid state, so comparing two states matches vanilla's identity comparisons.
/// </remarks>
internal readonly record struct FluidState(FluidKind Kind, int Amount, bool Falling)
{
    // A liquid block's state for each legacy level, per liquid (vanilla LiquidBlock.LEVEL).
    private static readonly IBlock[] waterBlocks = LiquidStates("minecraft:water");
    private static readonly IBlock[] lavaBlocks = LiquidStates("minecraft:lava");

    /// <summary>
    /// No fluid (vanilla <c>Fluids.EMPTY</c>).
    /// </summary>
    public static FluidState Empty => default;

    /// <summary>
    /// Whether there's no fluid.
    /// </summary>
    public bool IsEmpty => this.Kind == FluidKind.Empty;

    /// <summary>
    /// Whether the fluid is a source (a still liquid block or a waterlogged block's water).
    /// </summary>
    public bool IsSource => this.Kind is FluidKind.Water or FluidKind.Lava;

    /// <summary>
    /// Vanilla <c>is(FluidTags.WATER)</c>: source or flowing water.
    /// </summary>
    public bool IsWater => IsWaterKind(this.Kind);

    /// <summary>
    /// Vanilla <c>is(FluidTags.LAVA)</c>: source or flowing lava.
    /// </summary>
    public bool IsLava => this.Kind is FluidKind.Lava or FluidKind.FlowingLava;

    /// <summary>
    /// The fluid's behavior, or <c>null</c> for no fluid.
    /// </summary>
    public FlowingFluid? Type => this.IsWater ? WaterFluid.Instance : this.IsLava ? LavaFluid.Instance : null;

    /// <summary>
    /// Vanilla <c>getOwnHeight</c>: the fluid's surface height within its block, ignoring the fluid above.
    /// </summary>
    public float OwnHeight => this.Amount / 9f;

    /// <summary>
    /// The fluid of <paramref name="block"/>: a liquid block's level, or a water source for waterlogged blocks.
    /// </summary>
    public static FluidState Of(IBlock block)
    {
        var kind = block.GetFluid();
        if (kind == FluidKind.Empty)
            return Empty;

        // Only liquid blocks carry falling fluids: levels 8-15 hold a falling fluid of amount 8.
        var amount = block.FluidAmount();
        return new FluidState(kind, amount, block.IsLiquidBlock() && amount == 8 && !block.IsFluidSource());
    }

    /// <summary>
    /// Whether <paramref name="kind"/> is source or flowing water.
    /// </summary>
    public static bool IsWaterKind(FluidKind kind) => kind is FluidKind.Water or FluidKind.FlowingWater;

    /// <summary>
    /// Vanilla <c>getHeight</c>: a full block when the same fluid is above, otherwise <see cref="OwnHeight"/>.
    /// </summary>
    public float GetHeight(FluidLevel level, Vector position) =>
        this.IsEmpty ? 0 : this.Type!.IsSame(level.GetFluid(position + Vector.Up).Kind) ? 1 : this.OwnHeight;

    /// <summary>
    /// Vanilla <c>canBeReplacedWith</c>: whether <paramref name="fluid"/> may flow into this fluid's block from
    /// <paramref name="direction"/>.
    /// </summary>
    public bool CanBeReplacedWith(FluidLevel level, Vector position, FluidKind fluid, BlockFace direction) =>
        this.Type?.CanBeReplacedWith(this, level, position, fluid, direction) ?? true;

    /// <summary>
    /// Vanilla <c>createLegacyBlock</c>: the liquid block holding this fluid, or air for no fluid.
    /// </summary>
    public IBlock CreateLegacyBlock()
    {
        if (this.IsEmpty)
            return BlocksRegistry.Air;

        var level = this.IsSource ? 0 : 8 - Math.Min(this.Amount, 8) + (this.Falling ? 8 : 0);
        return (this.IsWater ? waterBlocks : lavaBlocks)[level];
    }

    private static IBlock[] LiquidStates(string name)
    {
        var block = BlockStateProperties.GetState(name);
        return [.. Enumerable.Range(0, 16).Select(level => block.WithProperty("level", level))];
    }
}
