using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Port of vanilla's <c>MultifaceSpreader</c> and the multiface block rules it needs (glow lichen and sculk vein), used by
/// <see cref="MultifaceGrowthFeature"/> and <see cref="SculkPatchFeature"/>.
/// </summary>
/// <remarks>
/// Vanilla evaluates spreads lazily: a random direction spread stops at the first success, and <see cref="SpreadAll"/>
/// always tries every face/direction pair against the original state.
/// </remarks>
internal sealed class MultifaceSpreader
{
    private static readonly BlockSet fire = new("#minecraft:fire");

    private readonly IBlock block;
    private readonly SpreadType[] spreadTypes;
    private readonly bool sculkVein;

    private MultifaceSpreader(IBlock block, SpreadType[] spreadTypes, bool sculkVein)
    {
        this.block = block;
        this.spreadTypes = spreadTypes;
        this.sculkVein = sculkVein;
    }

    public static readonly SpreadType[] DefaultSpreadOrder = [SpreadType.SamePosition, SpreadType.SamePlane, SpreadType.WrapAround];

    public static MultifaceSpreader GlowLichen => field ??= new(BlocksRegistry.Get(Material.GlowLichen), DefaultSpreadOrder, false);

    /// <summary>
    /// Sculk vein's main spreader (<c>SculkVeinBlock.getSpreader()</c>).
    /// </summary>
    public static MultifaceSpreader SculkVein => field ??= new(BlocksRegistry.Get(Material.SculkVein), DefaultSpreadOrder, true);

    /// <summary>
    /// Sculk vein's same-position spreader (<c>SculkVeinBlock.getSameSpaceSpreader()</c>).
    /// </summary>
    public static MultifaceSpreader SculkVeinSameSpace =>
        field ??= new(BlocksRegistry.Get(Material.SculkVein), [SpreadType.SamePosition], true);

    /// <summary>
    /// The spreader of a multiface block type (glow lichen or sculk vein).
    /// </summary>
    public static MultifaceSpreader For(IBlock block) => block.Material == Material.SculkVein ? SculkVein : GlowLichen;

    public IBlock Block => this.block;

    /// <summary>
    /// Vanilla <c>spreadFromFaceTowardRandomDirection</c>: tries all directions in shuffled order, stopping at the first spread.
    /// </summary>
    public bool SpreadFromFaceTowardRandomDirection(IBlock state, IWorldGenLevel level, Vector position, BlockFace fromFace, IRandomSource random)
    {
        foreach (var direction in FeatureHelpers.ShuffledCopy(FeatureHelpers.Directions, random))
        {
            if (this.SpreadFromFaceTowardDirection(state, level, position, fromFace, direction))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Vanilla <c>spreadAll</c>: spreads from every face the state can spread from toward every direction; returns the count.
    /// </summary>
    public long SpreadAll(IBlock state, IWorldGenLevel level, Vector position)
    {
        long count = 0;
        foreach (var face in FeatureHelpers.Directions)
        {
            if (!this.CanSpreadFrom(state, face))
                continue;

            foreach (var direction in FeatureHelpers.Directions)
            {
                if (this.SpreadFromFaceTowardDirection(state, level, position, face, direction))
                    count++;
            }
        }

        return count;
    }

    private bool SpreadFromFaceTowardDirection(IBlock state, IWorldGenLevel level, Vector position, BlockFace fromFace, BlockFace direction)
    {
        var target = this.GetSpreadPosition(state, level, position, fromFace, direction);
        if (target is null)
            return false;

        var (targetPosition, targetFace) = target.Value;
        var newState = this.GetStateForPlacement(level.GetBlock(targetPosition), level, targetPosition, targetFace);
        return newState is not null && level.SetBlock(targetPosition, newState);
    }

    private (Vector Position, BlockFace Face)? GetSpreadPosition(IBlock state, IWorldGenLevel level, Vector position, BlockFace fromFace,
        BlockFace direction)
    {
        if (FeatureHelpers.SameAxis(direction, fromFace))
            return null;

        if (!this.IsOtherBlockValidAsSource(state) && !(HasFace(state, fromFace) && !HasFace(state, direction)))
            return null;

        foreach (var type in this.spreadTypes)
        {
            var target = type switch
            {
                SpreadType.SamePosition => (position, direction),
                SpreadType.SamePlane => (position.Offset(direction), fromFace),
                _ => (position.Offset(direction).Offset(fromFace), direction.Opposite())
            };

            if (this.CanSpreadInto(level, position, target.Item1, target.Item2))
                return target;
        }

        return null;
    }

    private bool CanSpreadFrom(IBlock state, BlockFace face) => this.IsOtherBlockValidAsSource(state) || HasFace(state, face);

    // SculkVeinSpreaderConfig.isOtherBlockValidAsSource: anything but a sculk vein can seed veins.
    private bool IsOtherBlockValidAsSource(IBlock state) => this.sculkVein && state.Material != Material.SculkVein;

    private bool CanSpreadInto(IWorldGenLevel level, Vector from, Vector to, BlockFace face)
    {
        var state = level.GetBlock(to);
        return this.StateCanBeReplaced(level, from, to, face, state) && this.IsValidStateForPlacement(level, state, to, face);
    }

    private bool StateCanBeReplaced(IWorldGenLevel level, Vector from, Vector to, BlockFace face, IBlock state)
    {
        if (this.sculkVein)
        {
            var neighbor = level.GetBlock(to.Offset(face));
            if (neighbor.Material is Material.Sculk or Material.SculkCatalyst or Material.MovingPiston)
                return false;

            if (FeatureHelpers.DistManhattan(from, to) == 2 && level.GetBlock(from.Offset(face.Opposite())).IsFaceSturdy(face))
                return false;

            var fluid = state.GetFluid();
            if (fluid != FluidKind.Empty && fluid != FluidKind.Water)
                return false;

            if (fire.Contains(state))
                return false;

            if (state.CanBeReplaced())
                return true;
        }

        // DefaultSpreaderConfig: air, the same block, or a water source.
        return state.IsAir || state.RegistryId == this.block.RegistryId || state.Material == Material.Water && state.IsFluidSource();
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock.isValidStateForPlacement</c>: the face is free and the neighbor in that direction supports it.
    /// </summary>
    public bool IsValidStateForPlacement(IWorldGenLevel level, IBlock state, Vector position, BlockFace face)
    {
        if (state.RegistryId == this.block.RegistryId && HasFace(state, face))
            return false;

        return BlockSurvival.CanAttachTo(level.GetBlock(position.Offset(face)), face);
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock.getStateForPlacement(state, level, pos, face)</c>: adds <paramref name="face"/> to the existing
    /// multiface block, or creates one (waterlogged in a water source); <c>null</c> when the face can't be placed.
    /// </summary>
    public IBlock? GetStateForPlacement(IBlock existing, IWorldGenLevel level, Vector position, BlockFace face)
    {
        if (!this.IsValidStateForPlacement(level, existing, position, face))
            return null;

        IBlock state;
        if (existing.RegistryId == this.block.RegistryId)
            state = existing;
        else if (existing.GetFluid() == FluidKind.Water)
            state = this.block.WithProperty("waterlogged", true);
        else
            state = this.block;

        return state.WithProperty(FeatureHelpers.FaceName(face), true);
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock.hasFace</c>; non-multiface blocks have no faces.
    /// </summary>
    public static bool HasFace(IBlock state, BlockFace face) => state.GetProperty(FeatureHelpers.FaceName(face)) == "true"
        && state.BlockClass() is "MultifaceBlock" or "GlowLichenBlock" or "SculkVeinBlock";

    public enum SpreadType
    {
        SamePosition,
        SamePlane,
        WrapAround
    }
}
