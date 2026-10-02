using Obsidian.API.World.Generator.RandomSources;
using System.Runtime.CompilerServices;

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

    // Faces of each block state (see Faces), indexed by state id and computed on first use; -1 until then.
    private static readonly sbyte[] faceMasks = CreateFaceMasks();

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
        Span<BlockFace> directions = stackalloc BlockFace[FeatureHelpers.Directions.Length];
        FeatureHelpers.Directions.CopyTo(directions);
        FeatureHelpers.Shuffle(directions, random);

        var blocks = new Neighborhood(level, position);
        foreach (var direction in directions)
        {
            if (this.SpreadFromFaceTowardDirection(state, ref blocks, position, fromFace, direction))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Vanilla <c>spreadAll</c>: spreads from every face the state can spread from toward every direction; returns the count.
    /// </summary>
    public long SpreadAll(IBlock state, IWorldGenLevel level, Vector position)
    {
        var blocks = new Neighborhood(level, position);
        long count = 0;
        foreach (var face in FeatureHelpers.Directions)
        {
            if (!this.CanSpreadFrom(state, face))
                continue;

            foreach (var direction in FeatureHelpers.Directions)
            {
                if (this.SpreadFromFaceTowardDirection(state, ref blocks, position, face, direction))
                    count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock.getStateForPlacement(state, level, pos, face)</c>: adds <paramref name="face"/> to the existing
    /// multiface block, or creates one (waterlogged in a water source); <c>null</c> when the face can't be placed.
    /// </summary>
    public IBlock? GetStateForPlacement(IBlock existing, IWorldGenLevel level, Vector position, BlockFace face)
    {
        var blocks = new Neighborhood(level, position);
        return this.GetStateForPlacement(existing, ref blocks, position, face);
    }

    private bool SpreadFromFaceTowardDirection(IBlock state, ref Neighborhood blocks, Vector position, BlockFace fromFace, BlockFace direction)
    {
        var target = this.GetSpreadPosition(state, ref blocks, position, fromFace, direction);
        if (target is null)
            return false;

        var (targetPosition, targetFace) = target.Value;
        var newState = this.GetStateForPlacement(blocks.Get(targetPosition), ref blocks, targetPosition, targetFace);
        return newState is not null && blocks.Set(targetPosition, newState);
    }

    private (Vector Position, BlockFace Face)? GetSpreadPosition(IBlock state, ref Neighborhood blocks, Vector position, BlockFace fromFace,
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

            if (this.CanSpreadInto(ref blocks, position, target.Item1, target.Item2))
                return target;
        }

        return null;
    }

    private bool CanSpreadFrom(IBlock state, BlockFace face) => this.IsOtherBlockValidAsSource(state) || HasFace(state, face);

    // SculkVeinSpreaderConfig.isOtherBlockValidAsSource: anything but a sculk vein can seed veins.
    private bool IsOtherBlockValidAsSource(IBlock state) => this.sculkVein && state.Material != Material.SculkVein;

    private bool CanSpreadInto(ref Neighborhood blocks, Vector from, Vector to, BlockFace face)
    {
        var state = blocks.Get(to);
        return this.StateCanBeReplaced(ref blocks, from, to, face, state) && this.IsValidStateForPlacement(ref blocks, state, to, face);
    }

    private bool StateCanBeReplaced(ref Neighborhood blocks, Vector from, Vector to, BlockFace face, IBlock state)
    {
        if (this.sculkVein)
        {
            var neighbor = blocks.Get(to.Offset(face));
            if (neighbor.Material is Material.Sculk or Material.SculkCatalyst or Material.MovingPiston)
                return false;

            if (FeatureHelpers.DistManhattan(from, to) == 2 && blocks.Get(from.Offset(face.Opposite())).IsFaceSturdy(face))
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

    // Vanilla MultifaceBlock.isValidStateForPlacement: the face is free and the neighbor in that direction supports it.
    private bool IsValidStateForPlacement(ref Neighborhood blocks, IBlock state, Vector position, BlockFace face)
    {
        if (state.RegistryId == this.block.RegistryId && HasFace(state, face))
            return false;

        return BlockSurvival.CanAttachTo(blocks.Get(position.Offset(face)), face);
    }

    private IBlock? GetStateForPlacement(IBlock existing, ref Neighborhood blocks, Vector position, BlockFace face)
    {
        if (!this.IsValidStateForPlacement(ref blocks, existing, position, face))
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
    public static bool HasFace(IBlock state, BlockFace face) => (Faces(state) >> (int)face & 1) != 0;

    /// <summary>
    /// The faces of a multiface block state, one bit per <see cref="BlockFace"/> value; none for other blocks.
    /// </summary>
    public static int Faces(IBlock state)
    {
        var id = state.GetHashCode();
        if ((uint)id >= (uint)faceMasks.Length)
            return ComputeFaces(state);

        // Threads racing on an entry store the same value.
        var faces = faceMasks[id];
        if (faces < 0)
            faceMasks[id] = faces = (sbyte)ComputeFaces(state);

        return faces;
    }

    private static sbyte[] CreateFaceMasks()
    {
        var masks = new sbyte[BlocksRegistry.StateToNumeric.Length];
        Array.Fill(masks, (sbyte)-1);
        return masks;
    }

    private static int ComputeFaces(IBlock state)
    {
        if (state.BlockClass() is not ("MultifaceBlock" or "GlowLichenBlock" or "SculkVeinBlock"))
            return 0;

        var faces = 0;
        foreach (var face in FeatureHelpers.Directions)
        {
            if (state.GetProperty(FeatureHelpers.FaceName(face)) == "true")
                faces |= 1 << (int)face;
        }

        return faces;
    }

    public enum SpreadType
    {
        SamePosition,
        SamePlane,
        WrapAround
    }

    /// <summary>
    /// The level as a spread sees it, remembering the blocks it read around its position until it writes them: a spread
    /// checks the same few blocks many times, and only reads and writes within one block of its position.
    /// </summary>
    private ref struct Neighborhood(IWorldGenLevel level, Vector center)
    {
        private NeighborhoodBlocks blocks;

        public IBlock Get(Vector position)
        {
            var index = Index(position);
            return index < 0 ? level.GetBlock(position) : this.blocks[index] ??= level.GetBlock(position);
        }

        public bool Set(Vector position, IBlock block)
        {
            // Read the block again next time: the write may also be refused or ignored.
            var index = Index(position);
            if (index >= 0)
                this.blocks[index] = null;

            return level.SetBlock(position, block);
        }

        private readonly int Index(Vector position)
        {
            var dx = position.X - center.X + 1;
            var dy = position.Y - center.Y + 1;
            var dz = position.Z - center.Z + 1;
            return (uint)dx < 3 && (uint)dy < 3 && (uint)dz < 3 ? (dy * 3 + dz) * 3 + dx : -1;
        }
    }

    [InlineArray(27)]
    private struct NeighborhoodBlocks
    {
        private IBlock? first;
    }
}
