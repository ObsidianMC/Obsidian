using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.WorldData.Features;

/// <summary>
/// Deep dark sculk patches grown by simulating sculk charge spreading, plus an optional catalyst and shriekers, like vanilla's
/// SculkPatchFeature.
/// </summary>
[ConfiguredFeatureClass("minecraft:sculk_patch")]
public sealed class SculkPatchFeature : ConfiguredFeatureBase
{
    public override string Type => "minecraft:sculk_patch";

    public required int ChargeCount { get; init; }

    public required int AmountPerCharge { get; init; }

    public required int SpreadAttempts { get; init; }

    public required int GrowthRounds { get; init; }

    public required int SpreadRounds { get; init; }

    public required IIntProvider ExtraRareGrowths { get; init; }

    public required float CatalystChance { get; init; }

    private static IBlock SculkCatalyst => field ??= BlocksRegistry.Get(Material.SculkCatalyst);

    private static IBlock SculkShrieker => field ??= BlocksRegistry.Get(Material.SculkShrieker).WithProperty("can_summon", true);

    public override bool Place(FeatureContext context)
    {
        var level = context.Level;
        var random = context.Random;
        var origin = context.Origin;
        if (!level.EnsureCanWrite(origin) || !CanSpreadFrom(level, origin))
            return false;

        var spreader = new SculkSpreader();
        var rounds = this.SpreadRounds + this.GrowthRounds;
        for (var round = 0; round < rounds; round++)
        {
            for (var i = 0; i < this.ChargeCount; i++)
                spreader.AddCursors(origin, this.AmountPerCharge);

            var spreadVeins = round < this.SpreadRounds;
            for (var i = 0; i < this.SpreadAttempts; i++)
                spreader.UpdateCursors(level, origin, random, spreadVeins);

            spreader.Clear();
        }

        if (random.NextFloat() <= this.CatalystChance && level.GetBlock(origin + Vector.Down).IsCollisionShapeFullBlock())
            level.SetBlock(origin, SculkCatalyst);

        var growths = this.ExtraRareGrowths.Sample(random);
        for (var i = 0; i < growths; i++)
        {
            var position = origin + new Vector(random.NextInt(5) - 2, 0, random.NextInt(5) - 2);
            if (level.GetBlock(position).IsAir && level.GetBlock(position + Vector.Down).IsFaceSturdy(BlockFace.Up))
                level.SetBlock(position, SculkShrieker);
        }

        return true;
    }

    private static bool CanSpreadFrom(IWorldGenLevel level, Vector position)
    {
        var state = level.GetBlock(position);
        if (SculkSpreader.IsSculkBehaviour(state))
            return true;

        if (!state.IsAir && (state.Material != Material.Water || !state.IsFluidSource()))
            return false;

        foreach (var face in FeatureHelpers.Directions)
        {
            if (level.GetBlock(position.Offset(face)).IsCollisionShapeFullBlock())
                return true;
        }

        return false;
    }
}

/// <summary>
/// Worldgen port of vanilla's <c>SculkSpreader</c> with the sculk block, sculk vein and default behaviours.
/// </summary>
/// <remarks>
/// World generation settings: replaceable tag <c>sculk_replaceable_world_gen</c>, growth spawn cost 50, no-growth radius 1,
/// charge decay rate 5, additional decay rate 10; cursors never merge.
/// </remarks>
internal sealed class SculkSpreader
{
    private const int MaxCursors = 32;
    private const int GrowthSpawnCost = 50;
    private const int NoGrowthRadius = 1;
    private const int ChargeDecayRate = 5;
    private const int AdditionalDecayRate = 10;

    // A cursor's facings before it first lands on sculk (vanilla's null facings).
    private const int NoFacings = -1;

    private static readonly BlockSet replaceableWorldGen = new("#minecraft:sculk_replaceable_world_gen");
    private static readonly BlockSet sculkReplaceable = new("#minecraft:sculk_replaceable");

    // Registry ids of the blocks the cursors check by state id.
    private static readonly int sculkId = BlocksRegistry.Get(Material.Sculk).RegistryId;
    private static readonly int sculkVeinId = BlocksRegistry.Get(Material.SculkVein).RegistryId;
    private static readonly int sculkSensorId = BlocksRegistry.Get(Material.SculkSensor).RegistryId;
    private static readonly int sculkShriekerId = BlocksRegistry.Get(Material.SculkShrieker).RegistryId;

    // BlockPos.betweenClosed(-1..1) without corners and the center, in iteration order.
    private static readonly Vector[] nonCornerNeighbours = [.. FeatureHelpers.BetweenClosed(new Vector(-1), new Vector(1))
        .Where(offset => (offset.X == 0 || offset.Y == 0 || offset.Z == 0) && offset != Vector.Zero)];

    private static IBlock Sculk => field ??= BlocksRegistry.Get(Material.Sculk);

    private static IBlock SculkVein => field ??= BlocksRegistry.Get(Material.SculkVein);

    private static IBlock SculkSensor => field ??= BlocksRegistry.Get(Material.SculkSensor);

    private static IBlock SculkShriekerGrowth => field ??= BlocksRegistry.Get(Material.SculkShrieker).WithProperty("can_summon", true);

    private static IBlock Air => field ??= BlocksRegistry.Get(Material.Air);

    private static IBlock Water => field ??= BlocksRegistry.Get(Material.Water);

    private readonly ChargeCursor[] cursors = new ChargeCursor[MaxCursors];
    private int cursorCount;

    public static bool IsSculkBehaviour(IBlock block) => block.Material is Material.Sculk or Material.SculkVein;

    private static bool IsSculkBehaviour(int stateId)
    {
        var block = BlocksRegistry.RegistryIdOf(stateId);
        return block == sculkId || block == sculkVeinId;
    }

    public void Clear() => this.cursorCount = 0;

    public void AddCursors(Vector position, int amount)
    {
        while (amount > 0)
        {
            var charge = Math.Min(amount, 1000);
            if (this.cursorCount < MaxCursors)
                this.cursors[this.cursorCount++] = new ChargeCursor(position, charge);

            amount -= charge;
        }
    }

    public void UpdateCursors(IWorldGenLevel level, Vector origin, IRandomSource random, bool spreadVeins)
    {
        // The cursors that keep a charge move to the front, in order.
        var survivors = 0;
        for (var i = 0; i < this.cursorCount; i++)
        {
            ref var cursor = ref this.cursors[i];
            if (cursor.IsPosUnreasonable(origin))
                continue;

            cursor.Update(level, origin, random, spreadVeins);
            if (cursor.Charge > 0)
                this.cursors[survivors++] = cursor;
        }

        this.cursorCount = survivors;
    }

    private struct ChargeCursor(Vector position, int charge)
    {
        private int updateDelay;
        private int decayDelay = 1;

        // The faces (see MultifaceSpreader.Faces) of the last sculk block the cursor moved to, or NoFacings.
        private int facings = NoFacings;

        public Vector Position { get; private set; } = position;

        public int Charge { get; private set; } = charge;

        public readonly bool IsPosUnreasonable(Vector origin) =>
            Math.Max(Math.Max(Math.Abs(this.Position.X - origin.X), Math.Abs(this.Position.Y - origin.Y)), Math.Abs(this.Position.Z - origin.Z)) > 1024;

        public void Update(IWorldGenLevel level, Vector origin, IRandomSource random, bool spreadVeins)
        {
            if (this.Charge <= 0)
                return;

            if (this.updateDelay > 0)
            {
                this.updateDelay--;
                return;
            }

            var state = level.GetBlock(this.Position);
            var behaviour = GetBehaviour(state);
            if (spreadVeins && AttemptSpreadVein(behaviour, level, this.Position, state, this.facings) && behaviour != Behaviour.Sculk)
            {
                // Everything but plain sculk may change its block when spreading, so re-read it.
                state = level.GetBlock(this.Position);
                behaviour = GetBehaviour(state);
            }

            this.Charge = this.AttemptUseCharge(behaviour, level, origin, random, spreadVeins);
            if (this.Charge <= 0)
            {
                OnDischarged(behaviour, level, state, this.Position);
                return;
            }

            var next = GetValidMovementPos(level, this.Position, random);
            if (next is Vector moved)
            {
                OnDischarged(behaviour, level, state, this.Position);
                this.Position = moved;

                if (FeatureHelpers.DistSqr(moved, new Vector(origin.X, moved.Y, origin.Z)) >= 15.0 * 15.0)
                {
                    this.Charge = 0;
                    return;
                }

                state = level.GetBlock(moved);
            }

            if (IsSculkBehaviour(state))
                this.facings = MultifaceSpreader.Faces(state);

            // The behaviour of the block the cursor started on decides the delays, like vanilla.
            this.decayDelay = behaviour == Behaviour.Default ? Math.Max(this.decayDelay - 1, 0) : 1;
            this.updateDelay = 1;
        }

        private readonly int AttemptUseCharge(Behaviour behaviour, IWorldGenLevel level, Vector origin, IRandomSource random, bool spreadVeins)
        {
            switch (behaviour)
            {
                case Behaviour.Sculk:
                    return this.SculkUseCharge(level, origin, random);
                case Behaviour.Vein:
                    if (spreadVeins && AttemptPlaceSculk(level, this.Position, random))
                        return this.Charge - 1;

                    return random.NextInt(ChargeDecayRate) == 0 ? Mth.Floor(this.Charge * 0.5f) : this.Charge;
                default:
                    return this.decayDelay > 0 ? this.Charge : 0;
            }
        }

        // SculkBlock.attemptUseCharge.
        private readonly int SculkUseCharge(IWorldGenLevel level, Vector origin, IRandomSource random)
        {
            var charge = this.Charge;
            if (charge == 0 || random.NextInt(ChargeDecayRate) != 0)
                return charge;

            var near = FeatureHelpers.DistSqr(this.Position, origin) < (double)NoGrowthRadius * NoGrowthRadius;
            if (!near && CanPlaceGrowth(level, this.Position))
            {
                if (random.NextInt(GrowthSpawnCost) < charge)
                {
                    var above = this.Position + Vector.Up;
                    level.SetBlock(above, GetRandomGrowthState(level, above, random));
                }

                return Math.Max(0, charge - GrowthSpawnCost);
            }

            return random.NextInt(AdditionalDecayRate) != 0 ? charge : charge - (near ? 1 : DecayPenalty(this.Position, origin, charge));
        }

        private static int DecayPenalty(Vector position, Vector origin, int charge)
        {
            var distance = (float)Math.Sqrt(FeatureHelpers.DistSqr(position, origin)) - NoGrowthRadius;
            var scale = Math.Min(1.0f, distance * distance / ((24 - NoGrowthRadius) * (24 - NoGrowthRadius)));
            return Math.Max(1, (int)(charge * scale * 0.5f));
        }

        private static IBlock GetRandomGrowthState(IWorldGenLevel level, Vector position, IRandomSource random)
        {
            var state = random.NextInt(11) == 0 ? SculkShriekerGrowth : SculkSensor;
            return state.HasProperty("waterlogged") && level.GetBlock(position).HasFluid() ? state.WithProperty("waterlogged", true) : state;
        }

        // Vanilla's BlockPos.betweenClosed over the 9x3x9 box above the sculk, with at most two growths in it.
        private static bool CanPlaceGrowth(IWorldGenLevel level, Vector position)
        {
            var above = level.GetBlock(position + Vector.Up);
            if (!above.IsAir && !(above.Material == Material.Water && above.GetFluid() == FluidKind.Water))
                return false;

            var growths = 0;
            for (var z = position.Z - 4; z <= position.Z + 4; z++)
            {
                for (var y = position.Y; y <= position.Y + 2; y++)
                {
                    for (var x = position.X - 4; x <= position.X + 4; x++)
                    {
                        var block = BlocksRegistry.RegistryIdOf(level.GetStateId(new Vector(x, y, z)));
                        if (block == sculkSensorId || block == sculkShriekerId)
                            growths++;

                        if (growths > 2)
                            return false;
                    }
                }
            }

            return true;
        }

        private static Vector? GetValidMovementPos(IWorldGenLevel level, Vector position, IRandomSource random)
        {
            Span<Vector> offsets = stackalloc Vector[nonCornerNeighbours.Length];
            nonCornerNeighbours.CopyTo(offsets);
            FeatureHelpers.Shuffle(offsets, random);

            var result = position;
            foreach (var offset in offsets)
            {
                var candidate = position + offset;
                var state = level.GetStateId(candidate);
                if (!IsSculkBehaviour(state) || !IsMovementUnobstructed(level, position, candidate))
                    continue;

                result = candidate;
                if (HasSubstrateAccess(level, state, candidate))
                    break;
            }

            return result == position ? null : result;
        }

        private static bool IsMovementUnobstructed(IWorldGenLevel level, Vector from, Vector to)
        {
            if (FeatureHelpers.DistManhattan(from, to) == 1)
                return true;

            var delta = to - from;
            var x = delta.X < 0 ? BlockFace.West : BlockFace.East;
            var y = delta.Y < 0 ? BlockFace.Down : BlockFace.Up;
            var z = delta.Z < 0 ? BlockFace.North : BlockFace.South;

            if (delta.X == 0)
                return IsUnobstructed(level, from, y) || IsUnobstructed(level, from, z);

            return delta.Y == 0
                ? IsUnobstructed(level, from, x) || IsUnobstructed(level, from, z)
                : IsUnobstructed(level, from, x) || IsUnobstructed(level, from, y);
        }

        private static bool IsUnobstructed(IWorldGenLevel level, Vector position, BlockFace face) =>
            !BlockPhysics.IsFaceSturdy(level.GetStateId(position.Offset(face)), face.Opposite());
    }

    private enum Behaviour
    {
        Default,
        Sculk,
        Vein
    }

    private static Behaviour GetBehaviour(IBlock state) => state.Material switch
    {
        Material.Sculk => Behaviour.Sculk,
        Material.SculkVein => Behaviour.Vein,
        _ => Behaviour.Default
    };

    private static bool AttemptSpreadVein(Behaviour behaviour, IWorldGenLevel level, Vector position, IBlock state, int facings)
    {
        if (behaviour != Behaviour.Default)
            return MultifaceSpreader.SculkVein.SpreadAll(state, level, position) > 0;

        if (facings == NoFacings)
            return MultifaceSpreader.SculkVeinSameSpace.SpreadAll(level.GetBlock(position), level, position) > 0;

        if (facings == 0)
            return MultifaceSpreader.SculkVein.SpreadAll(state, level, position) > 0;

        if (!state.IsAir && state.GetFluid() != FluidKind.Water)
            return false;

        return Regrow(level, position, state, facings);
    }

    // SculkVeinBlock.regrow: a vein on every listed face that still has support.
    private static bool Regrow(IWorldGenLevel level, Vector position, IBlock state, int faces)
    {
        var vein = SculkVein;
        var any = false;
        foreach (var face in FeatureHelpers.Directions)
        {
            if ((faces >> (int)face & 1) != 0 && BlockSurvival.CanAttachTo(level.GetBlock(position.Offset(face)), face))
            {
                vein = vein.WithProperty(FeatureHelpers.FaceName(face), true);
                any = true;
            }
        }

        if (!any)
            return false;

        if (state.HasFluid())
            vein = vein.WithProperty("waterlogged", true);

        level.SetBlock(position, vein);
        return true;
    }

    // SculkVeinBlock.attemptPlaceSculk: turn a replaceable block behind one of the vein's faces into sculk.
    private static bool AttemptPlaceSculk(IWorldGenLevel level, Vector position, IRandomSource random)
    {
        var state = level.GetBlock(position);
        Span<BlockFace> faces = stackalloc BlockFace[FeatureHelpers.Directions.Length];
        FeatureHelpers.Directions.CopyTo(faces);
        FeatureHelpers.Shuffle(faces, random);

        foreach (var face in faces)
        {
            if (!MultifaceSpreader.HasFace(state, face))
                continue;

            var target = position.Offset(face);
            if (!replaceableWorldGen.Contains(level.GetBlock(target)))
                continue;

            level.SetBlock(target, Sculk);
            MultifaceSpreader.SculkVein.SpreadAll(Sculk, level, target);

            var back = face.Opposite();
            foreach (var direction in FeatureHelpers.Directions)
            {
                if (direction == back)
                    continue;

                var neighborPosition = target.Offset(direction);
                var neighbor = level.GetBlock(neighborPosition);
                if (neighbor.Material == Material.SculkVein)
                    OnDischarged(Behaviour.Vein, level, neighbor, neighborPosition);
            }

            return true;
        }

        return false;
    }

    // SculkVeinBlock.onDischarged: drop faces now covered by sculk; an empty vein becomes air or water.
    private static void OnDischarged(Behaviour behaviour, IWorldGenLevel level, IBlock state, Vector position)
    {
        if (behaviour != Behaviour.Vein || state.Material != Material.SculkVein)
            return;

        foreach (var face in FeatureHelpers.Directions)
        {
            if (MultifaceSpreader.HasFace(state, face) && level.GetBlock(position.Offset(face)).Material == Material.Sculk)
                state = state.WithProperty(FeatureHelpers.FaceName(face), false);
        }

        if (MultifaceSpreader.Faces(state) == 0)
            state = level.GetBlock(position).HasFluid() ? Water : Air;

        level.SetBlock(position, state);
    }

    private static bool HasSubstrateAccess(IWorldGenLevel level, int stateId, Vector position)
    {
        if (BlocksRegistry.RegistryIdOf(stateId) != sculkVeinId)
            return false;

        var faces = MultifaceSpreader.Faces(stateId);
        foreach (var face in FeatureHelpers.Directions)
        {
            if ((faces >> (int)face & 1) != 0 && sculkReplaceable.ContainsState(level.GetStateId(position.Offset(face))))
                return true;
        }

        return false;
    }
}
