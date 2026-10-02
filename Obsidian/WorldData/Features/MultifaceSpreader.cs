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
    // What spreading needs to know about a block state (see Info): bits 0-5 are its multiface faces and bits 6-11 its
    // sturdy faces, one per BlockFace value.
    private const int FacesMask = 0x3F;
    private const int SturdyFacesShift = 6;
    private const int FullCollisionBit = 1 << 12;
    // Sculk, sculk catalysts and moving pistons, which veins don't spread next to.
    private const int BlocksVeinsBit = 1 << 13;
    // A fluid other than still water, or fire.
    private const int RejectsVeinsBit = 1 << 14;
    private const int ReplaceableBit = 1 << 15;
    // Air or a water source.
    private const int DefaultReplaceableBit = 1 << 16;
    private const int WaterBit = 1 << 17;
    private const int GlowLichenBit = 1 << 18;
    private const int SculkVeinBit = 1 << 19;
    private const int KnownBit = 1 << 30;

    private static readonly BlockSet fire = new("#minecraft:fire");

    // Info of each block state, indexed by state id and computed on first use; 0 until then.
    private static readonly int[] infos = new int[BlocksRegistry.StateToNumeric.Length];

    private static readonly int glowLichenId = BlocksRegistry.Get(Material.GlowLichen).RegistryId;
    private static readonly int sculkVeinId = BlocksRegistry.Get(Material.SculkVein).RegistryId;

    private readonly IBlock block;
    private readonly SpreadType[] spreadTypes;
    private readonly bool sculkVein;

    // The info bit of the spreader's block.
    private readonly int sameBlockBit;

    private MultifaceSpreader(IBlock block, SpreadType[] spreadTypes, bool sculkVein)
    {
        this.block = block;
        this.spreadTypes = spreadTypes;
        this.sculkVein = sculkVein;
        this.sameBlockBit = Info(block) & (GlowLichenBit | SculkVeinBit);
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

    private IBlock WaterloggedBlock => field ??= this.block.WithProperty("waterlogged", true);

    /// <summary>
    /// Vanilla <c>spreadFromFaceTowardRandomDirection</c>: tries all directions in shuffled order, stopping at the first spread.
    /// </summary>
    public bool SpreadFromFaceTowardRandomDirection(IBlock state, IWorldGenLevel level, Vector position, BlockFace fromFace, IRandomSource random)
    {
        Span<BlockFace> directions = stackalloc BlockFace[FeatureHelpers.Directions.Length];
        FeatureHelpers.Directions.CopyTo(directions);
        FeatureHelpers.Shuffle(directions, random);

        var blocks = new Neighborhood(level, position);
        var stateInfo = Info(state);
        foreach (var direction in directions)
        {
            if (this.SpreadFromFaceTowardDirection(stateInfo, ref blocks, position, fromFace, direction))
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
        var stateInfo = Info(state);
        long count = 0;
        foreach (var face in FeatureHelpers.Directions)
        {
            if (!this.CanSpreadFrom(stateInfo, face))
                continue;

            foreach (var direction in FeatureHelpers.Directions)
            {
                if (this.SpreadFromFaceTowardDirection(stateInfo, ref blocks, position, face, direction))
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
        return this.GetStateForPlacement(existing, Info(existing), ref blocks, position, face);
    }

    /// <summary>
    /// Vanilla <c>MultifaceBlock.hasFace</c>; non-multiface blocks have no faces.
    /// </summary>
    public static bool HasFace(IBlock state, BlockFace face) => (Faces(state) >> (int)face & 1) != 0;

    /// <summary>
    /// The faces of a multiface block state, one bit per <see cref="BlockFace"/> value; none for other blocks.
    /// </summary>
    public static int Faces(IBlock state) => Info(state) & FacesMask;

    private bool SpreadFromFaceTowardDirection(int stateInfo, ref Neighborhood blocks, Vector position, BlockFace fromFace, BlockFace direction)
    {
        var target = this.GetSpreadPosition(stateInfo, ref blocks, position, fromFace, direction);
        if (target is null)
            return false;

        var (targetPosition, targetFace) = target.Value;
        var newState = this.GetStateForPlacement(blocks.Get(targetPosition), blocks.Info(targetPosition), ref blocks, targetPosition, targetFace);
        return newState is not null && blocks.Set(targetPosition, newState);
    }

    private (Vector Position, BlockFace Face)? GetSpreadPosition(int stateInfo, ref Neighborhood blocks, Vector position, BlockFace fromFace,
        BlockFace direction)
    {
        if (FeatureHelpers.SameAxis(direction, fromFace))
            return null;

        if (!this.IsOtherBlockValidAsSource(stateInfo) && !(HasFace(stateInfo, fromFace) && !HasFace(stateInfo, direction)))
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

    private bool CanSpreadFrom(int stateInfo, BlockFace face) => this.IsOtherBlockValidAsSource(stateInfo) || HasFace(stateInfo, face);

    // SculkVeinSpreaderConfig.isOtherBlockValidAsSource: anything but a sculk vein can seed veins.
    private bool IsOtherBlockValidAsSource(int stateInfo) => this.sculkVein && (stateInfo & SculkVeinBit) == 0;

    private bool CanSpreadInto(ref Neighborhood blocks, Vector from, Vector to, BlockFace face)
    {
        var stateInfo = blocks.Info(to);
        return this.StateCanBeReplaced(ref blocks, from, to, face, stateInfo) && this.IsValidStateForPlacement(ref blocks, stateInfo, to, face);
    }

    private bool StateCanBeReplaced(ref Neighborhood blocks, Vector from, Vector to, BlockFace face, int stateInfo)
    {
        if (this.sculkVein)
        {
            if ((blocks.Info(to.Offset(face)) & BlocksVeinsBit) != 0)
                return false;

            if (FeatureHelpers.DistManhattan(from, to) == 2 && IsFaceSturdy(blocks.Info(from.Offset(face.Opposite())), face))
                return false;

            if ((stateInfo & RejectsVeinsBit) != 0)
                return false;

            if ((stateInfo & ReplaceableBit) != 0)
                return true;
        }

        // DefaultSpreaderConfig: air, the same block, or a water source.
        return (stateInfo & (DefaultReplaceableBit | this.sameBlockBit)) != 0;
    }

    // Vanilla MultifaceBlock.isValidStateForPlacement: the face is free and the neighbor in that direction supports it
    // (BlockSurvival.CanAttachTo).
    private bool IsValidStateForPlacement(ref Neighborhood blocks, int stateInfo, Vector position, BlockFace face)
    {
        if ((stateInfo & this.sameBlockBit) != 0 && HasFace(stateInfo, face))
            return false;

        var neighbor = blocks.Info(position.Offset(face));
        return IsFaceSturdy(neighbor, face.Opposite()) || (neighbor & FullCollisionBit) != 0;
    }

    private IBlock? GetStateForPlacement(IBlock existing, int existingInfo, ref Neighborhood blocks, Vector position, BlockFace face)
    {
        if (!this.IsValidStateForPlacement(ref blocks, existingInfo, position, face))
            return null;

        IBlock state;
        if ((existingInfo & this.sameBlockBit) != 0)
            state = existing;
        else if ((existingInfo & WaterBit) != 0)
            state = this.WaterloggedBlock;
        else
            state = this.block;

        return state.WithProperty(FeatureHelpers.FaceName(face), true);
    }

    private static bool HasFace(int info, BlockFace face) => (info >> (int)face & 1) != 0;

    private static bool IsFaceSturdy(int info, BlockFace face) => (info >> (SturdyFacesShift + (int)face) & 1) != 0;

    private static int Info(IBlock state)
    {
        var id = state.GetHashCode();
        if ((uint)id >= (uint)infos.Length)
            return ComputeInfo(state);

        // Threads racing on an entry store the same value.
        var info = infos[id];
        if (info == 0)
            infos[id] = info = ComputeInfo(state);

        return info;
    }

    private static int ComputeInfo(IBlock state)
    {
        var info = KnownBit;
        if (state.BlockClass() is "MultifaceBlock" or "GlowLichenBlock" or "SculkVeinBlock")
        {
            foreach (var face in FeatureHelpers.Directions)
            {
                if (state.GetProperty(FeatureHelpers.FaceName(face)) == "true")
                    info |= 1 << (int)face;
            }
        }

        foreach (var face in FeatureHelpers.Directions)
        {
            if (state.IsFaceSturdy(face))
                info |= 1 << (SturdyFacesShift + (int)face);
        }

        if (state.IsCollisionShapeFullBlock())
            info |= FullCollisionBit;

        if (state.Material is Material.Sculk or Material.SculkCatalyst or Material.MovingPiston)
            info |= BlocksVeinsBit;

        var fluid = state.GetFluid();
        if (fluid != FluidKind.Empty && fluid != FluidKind.Water || fire.Contains(state))
            info |= RejectsVeinsBit;

        if (state.CanBeReplaced())
            info |= ReplaceableBit;

        if (state.IsAir || state.Material == Material.Water && state.IsFluidSource())
            info |= DefaultReplaceableBit;

        if (fluid == FluidKind.Water)
            info |= WaterBit;

        if (state.RegistryId == glowLichenId)
            info |= GlowLichenBit;

        if (state.RegistryId == sculkVeinId)
            info |= SculkVeinBit;

        return info;
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
        private NeighborhoodInfos infos;

        public IBlock Get(Vector position)
        {
            var index = this.Index(position);
            if (index < 0)
                return level.GetBlock(position);

            if (this.infos[index] == 0)
                this.Read(index, position);

            return this.blocks[index]!;
        }

        public int Info(Vector position)
        {
            var index = this.Index(position);
            if (index < 0)
                return MultifaceSpreader.Info(level.GetBlock(position));

            if (this.infos[index] == 0)
                this.Read(index, position);

            return this.infos[index];
        }

        public bool Set(Vector position, IBlock block)
        {
            // Read the block again next time: the write may also be refused or ignored.
            var index = this.Index(position);
            if (index >= 0)
                this.infos[index] = 0;

            return level.SetBlock(position, block);
        }

        private void Read(int index, Vector position)
        {
            var block = level.GetBlock(position);
            this.blocks[index] = block;
            this.infos[index] = MultifaceSpreader.Info(block);
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

    [InlineArray(27)]
    private struct NeighborhoodInfos
    {
        private int first;
    }
}
