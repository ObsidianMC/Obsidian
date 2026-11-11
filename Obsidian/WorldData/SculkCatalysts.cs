using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Registry.Codecs.Biomes;
using Obsidian.API.World.Features;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities.AI;
using Obsidian.Nbt;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using System.Threading;

namespace Obsidian.WorldData;

/// <summary>
/// A sculk catalyst's block entity, like vanilla's <c>SculkCatalystBlockEntity</c>: it takes the experience of mobs
/// that die near it as sculk charge (see <see cref="AbstractLevel.OfferDeathToSculkCatalysts"/>), and spreads that
/// charge every tick (see <see cref="SculkCharge"/>). Its charge cursors are saved as vanilla saves them.
/// </summary>
internal sealed class SculkCatalystBlockEntity : BlockEntity
{
    /// <summary>
    /// How far a death reaches it, measured between floored block positions (vanilla's listener radius).
    /// </summary>
    public const int ListenerRadius = 8;

    // Vanilla's PULSE_TICKS: how long the catalyst blooms.
    private const int BloomTicks = 8;

    private NbtCompound retained = new();

    /// <summary>
    /// The charge the catalyst spreads.
    /// </summary>
    public SculkCharge Charge { get; } = new();

    /// <summary>
    /// Vanilla's <c>CatalystListener.handleGameEvent</c> for a mob's death whose experience no catalyst took yet:
    /// the mob's experience, if it has any, becomes charge half a block above where it died, and the catalyst blooms.
    /// </summary>
    /// <remarks>
    /// The bloom's sound draws its pitch from <paramref name="random"/>, the level's random, as vanilla's does. Call it
    /// under the level's <see cref="Fluids.LevelFluids.TickLock"/>.
    /// </remarks>
    /// <returns>Whether the experience became charge.</returns>
    public bool TakeDeath(AbstractLevel level, VectorD position, int experience, IRandomSource random)
    {
        var charged = experience > 0;
        if (charged)
        {
            var above = new VectorD(position.X, position.Y + 0.5, position.Z);
            this.Charge.AddCursors((Vector)above.Floor(), experience);
            this.SetChanged();
        }

        this.Bloom(level, random);
        return charged;
    }

    /// <summary>
    /// The block entity tick: the charge spreads, with the level's random.
    /// </summary>
    public void Tick(AbstractLevel level)
    {
        if (this.Charge.Update(level, this.BlockPosition, level.GrowthRandom))
            this.SetChanged();
    }

    protected internal override ValueTask TickAsync()
    {
        if (this.Level is AbstractLevel level)
        {
            lock (level.Fluids.TickLock)
            {
                this.Tick(level);
            }
        }

        return ValueTask.CompletedTask;
    }

    protected internal override void Load(NbtCompound tag)
    {
        var copy = DataBlockEntity.Copy(tag);
        this.Charge.Load(copy.TryGetTag("cursors", out var cursors) ? cursors as NbtList : null);
        copy.Remove("cursors");
        Volatile.Write(ref this.retained, copy);
    }

    protected internal override void Save(NbtCompound tag)
    {
        foreach (var (name, value) in DataBlockEntity.Copy(Volatile.Read(ref this.retained)))
            tag.Add(name, value);

        tag.Add(this.Charge.Save());
    }

    // Vanilla's bloom: the catalyst blooms for eight ticks, with its particles and sound.
    private void Bloom(AbstractLevel level, IRandomSource random)
    {
        var position = this.BlockPosition;
        var bloomed = BlocksRegistry.SculkCatalyst.WithProperty("bloom", true);
        level.BlockUpdates.SetBlock(position, bloomed, BlockUpdateFlags.All);
        level.BlockUpdates.ScheduleTick(position, bloomed, BloomTicks);

        var particles = new VectorD(position.X + 0.5, position.Y + 1.15, position.Z + 0.5);
        level.SpawnParticles(new SculkSoulParticle(), particles, 2, new VectorF(0.2f, 0, 0.2f));

        var pitch = 0.6f + random.NextFloat() * 0.4f;
        BlockLighting.Play(level, position, "minecraft:block.sculk_catalyst.bloom", volume: 2, pitch: pitch);
    }

    private sealed class SculkSoulParticle : ParticleData
    {
        public override ParticleType ParticleType => ParticleType.SculkSoul;
    }
}

/// <summary>
/// Vanilla's <c>SculkCatalystBlock.tick</c>: a bloomed catalyst stops blooming.
/// </summary>
internal sealed class SculkCatalystBlockBehavior : IScheduledTickBehavior
{
    public void Tick(IBlockLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (block.GetProperty("bloom") == "true")
            level.SetBlock(position, block.WithProperty("bloom", false), BlockUpdateFlags.All);
    }
}

public abstract partial class AbstractLevel
{
    /// <summary>
    /// A mob died at <paramref name="position"/>: the sculk catalysts within reach of it, nearest first, take its
    /// death as vanilla's catalysts take the <c>entity_die</c> game event. The first takes the mob's experience as
    /// charge, which the mob then doesn't drop, and blooms; the others see it taken and do nothing.
    /// </summary>
    /// <remarks>
    /// The tick lock is taken only when a catalyst is near, and the catalysts are found again under it.
    /// </remarks>
    /// <param name="position">Where the mob died.</param>
    /// <param name="experience">The experience the mob would drop, read only if a catalyst takes it.</param>
    /// <param name="charged">Whether the experience became charge (the mob had some to drop).</param>
    /// <returns>Whether a catalyst took the mob's experience.</returns>
    internal bool OfferDeathToSculkCatalysts(VectorD position, Func<int> experience, out bool charged)
    {
        charged = false;
        if (this.NearestSculkCatalyst(position) is null)
            return false;

        lock (this.Fluids.TickLock)
        {
            if (this.NearestSculkCatalyst(position) is not SculkCatalystBlockEntity catalyst)
                return false;

            charged = catalyst.TakeDeath(this, position, experience(), this.GrowthRandom);
            return true;
        }
    }

    // Vanilla filters listeners by floored block positions, then orders those in range by exact center distance.
    private SculkCatalystBlockEntity? NearestSculkCatalyst(VectorD position)
    {
        const int Radius = SculkCatalystBlockEntity.ListenerRadius;
        var minChunkX = ((int)Math.Floor(position.X) - Radius) >> 4;
        var maxChunkX = ((int)Math.Floor(position.X) + Radius) >> 4;
        var minChunkZ = ((int)Math.Floor(position.Z) - Radius) >> 4;
        var maxChunkZ = ((int)Math.Floor(position.Z) + Radius) >> 4;

        SculkCatalystBlockEntity? nearest = null;
        var nearestDistance = double.MaxValue;
        for (var chunkX = minChunkX; chunkX <= maxChunkX; chunkX++)
        {
            for (var chunkZ = minChunkZ; chunkZ <= maxChunkZ; chunkZ++)
            {
                if (this.GetLoadedChunk(chunkX, chunkZ) is not Chunk chunk)
                    continue;

                foreach (var blockEntity in chunk.BlockEntities.Values)
                {
                    if (blockEntity is not SculkCatalystBlockEntity { IsRemoved: false } catalyst)
                        continue;

                    var blockOffset = (VectorD)catalyst.BlockPosition - position.Floor();
                    var blockDistance = blockOffset.X * blockOffset.X + blockOffset.Y * blockOffset.Y
                        + blockOffset.Z * blockOffset.Z;
                    if (blockDistance > Radius * Radius)
                        continue;

                    var offset = (VectorD)catalyst.BlockPosition + 0.5 - position;
                    var distance = offset.X * offset.X + offset.Y * offset.Y + offset.Z * offset.Z;
                    if (distance < nearestDistance)
                    {
                        nearest = catalyst;
                        nearestDistance = distance;
                    }
                }
            }
        }

        return nearest;
    }
}

/// <summary>
/// The sculk charge a catalyst spreads, like vanilla's <c>SculkSpreader.createLevelSpreader()</c> with its
/// <c>ChargeCursor</c>s and the <c>SculkBehaviour</c>s of sculk, sculk veins and every other block.
/// </summary>
/// <remarks>
/// The draws, writes (with vanilla's flags) and the charge particles' level events are vanilla's, in its order, which
/// the <c>ticking_blocks</c> capture checks. World generation's sculk patches spread with
/// <see cref="Features.SculkPatchFeature"/>'s own spreader.
/// </remarks>
internal sealed class SculkCharge
{
    /// <summary>
    /// Vanilla's <c>LevelEvent.PARTICLES_SCULK_CHARGE</c>.
    /// </summary>
    public const int ChargeEvent = 3006;

    private const int MaxCharge = 1000;
    private const int MaxCursors = 32;
    private const int MaxCursorDistance = 1024;
    private const int GrowthSpawnCost = 10;
    private const int NoGrowthRadius = 4;
    private const int ChargeDecayRate = 10;
    private const int AdditionalDecayRate = 5;
    private const int ShriekerRate = 11;
    private const int MaxGrowthRadius = 24;

    private static readonly WorldgenTags.BlockGroup replaceable = WorldgenTags.Blocks("sculk_replaceable");
    private static readonly WorldgenTags.BlockGroup growthInhibitors = WorldgenTags.Blocks("sculk_growth_inhibitors");

    // BlockPos.betweenClosed(-1..1) without the corners and the center, in vanilla's order, which its shuffle starts
    // from.
    private static readonly Vector[] nonCornerNeighbours = [.. FeatureHelpers.BetweenClosed(new Vector(-1), new Vector(1))
        .Where(offset => (offset.X == 0 || offset.Y == 0 || offset.Z == 0) && offset != Vector.Zero)];

    private List<Cursor> cursors = [];

    /// <summary>
    /// The charge cursors, in vanilla's order.
    /// </summary>
    public IReadOnlyList<Cursor> Cursors => this.cursors;

    /// <summary>
    /// Vanilla's <c>addCursors</c>: the charge in cursors of up to a thousand, as many as fit in 32.
    /// </summary>
    public void AddCursors(Vector position, int charge)
    {
        while (charge > 0)
        {
            var part = Math.Min(charge, MaxCharge);
            if (this.cursors.Count < MaxCursors)
                this.cursors.Add(new Cursor(position, part));

            charge -= part;
        }
    }

    /// <summary>
    /// Vanilla's <c>updateCursors</c> with veins spreading: each cursor moves its charge on, cursors that meet merge,
    /// and players see where the charge is.
    /// </summary>
    /// <returns>Whether there were cursors, so the saved data may have changed.</returns>
    public bool Update(AbstractLevel level, Vector origin, IRandomSource random)
    {
        if (this.cursors.Count == 0)
            return false;

        var view = new SculkLevel(level);
        var processed = new List<Cursor>();
        var mergeable = new Dictionary<Vector, Cursor>();
        var charges = new ChargeTable();

        foreach (var cursor in this.cursors)
        {
            if (cursor.IsUnreasonable(origin))
                continue;

            cursor.Update(view, origin, random);
            if (cursor.Charge <= 0)
            {
                level.BroadcastLevelEvent(ChargeEvent, cursor.Position, 0);
                continue;
            }

            var position = cursor.Position;
            charges.Add(position, cursor.Charge);
            if (!mergeable.TryGetValue(position, out var existing))
            {
                mergeable[position] = cursor;
                processed.Add(cursor);
            }
            else if (cursor.Charge + existing.Charge <= MaxCharge)
            {
                existing.MergeWith(cursor);
            }
            else
            {
                processed.Add(cursor);
                if (cursor.Charge < existing.Charge)
                    mergeable[position] = cursor;
            }
        }

        foreach (var (position, charge) in charges.InVanillaOrder())
        {
            if (charge <= 0 || mergeable[position].Facings is not int faces)
                continue;

            // Vanilla divides by the float 2.3, widened to a double.
            var particles = (int)(Math.Log(1.0 + charge) / (double)2.3f) + 1;
            level.BroadcastLevelEvent(ChargeEvent, position, (particles << 6) + faces);
        }

        this.cursors = processed;
        return true;
    }

    /// <summary>
    /// The cursors as vanilla saves them: a <c>cursors</c> list of each one's <c>pos</c>, and its <c>charge</c>,
    /// <c>decay_delay</c>, <c>update_delay</c> and <c>facings</c> where they aren't their defaults.
    /// </summary>
    public NbtList Save()
    {
        var list = new NbtList(NbtTagType.Compound, "cursors");
        foreach (var cursor in this.cursors)
            list.Add(cursor.Save());

        return list;
    }

    /// <summary>
    /// Reads the cursors <see cref="Save"/> wrote (vanilla's), at most 32 of them.
    /// </summary>
    public void Load(NbtList? list)
    {
        this.cursors.Clear();
        if (list is null)
            return;

        foreach (var tag in list)
        {
            if (tag is NbtCompound compound && Cursor.Load(compound) is Cursor cursor && this.cursors.Count < MaxCursors)
                this.cursors.Add(cursor);
        }
    }

    /// <summary>
    /// Vanilla's <c>ChargeCursor</c>: a charge on its way through sculk.
    /// </summary>
    internal sealed class Cursor(Vector position, int charge)
    {
        private int updateDelay;

        public Vector Position { get; private set; } = position;

        public int Charge { get; private set; } = charge;

        public int DecayDelay { get; private set; } = 1;

        public int UpdateDelay => this.updateDelay;

        /// <summary>
        /// The faces (as <see cref="MultifaceSpreader.Faces(IBlock)"/> packs them) of the last sculk it stood on, or
        /// null before it stood on any (vanilla's null facings).
        /// </summary>
        public int? Facings { get; private set; }

        public bool IsUnreasonable(Vector origin) =>
            Math.Max(Math.Max(Math.Abs(this.Position.X - origin.X), Math.Abs(this.Position.Y - origin.Y)),
                Math.Abs(this.Position.Z - origin.Z)) > MaxCursorDistance;

        public void MergeWith(Cursor other)
        {
            this.Charge += other.Charge;
            other.Charge = 0;
            this.updateDelay = Math.Min(this.updateDelay, other.updateDelay);
        }

        // Vanilla's ChargeCursor.update, in its order: the vein spread, the charge used, then the move.
        public void Update(SculkLevel level, Vector origin, IRandomSource random)
        {
            if (this.Charge <= 0)
                return;

            if (this.updateDelay > 0)
            {
                this.updateDelay--;
                return;
            }

            var state = level.GetBlock(this.Position);
            var behaviour = BehaviourOf(state);
            if (AttemptSpreadVein(behaviour, level, this.Position, state, this.Facings))
            {
                // Plain sculk keeps its block when it spreads; the others read theirs again.
                if (behaviour != Behaviour.Sculk)
                {
                    state = level.GetBlock(this.Position);
                    behaviour = BehaviourOf(state);
                }

                level.PlaySound(this.Position, "minecraft:block.sculk.spread");
            }

            this.Charge = this.UseCharge(behaviour, level, origin, random);
            if (this.Charge <= 0)
            {
                OnDischarged(behaviour, level, state, this.Position);
                return;
            }

            if (MovementTarget(level, this.Position, random) is Vector moved)
            {
                OnDischarged(behaviour, level, state, this.Position);
                this.Position = moved;
                state = level.GetBlock(moved);
            }

            if (BehaviourOf(state) != Behaviour.Default)
                this.Facings = MultifaceSpreader.Faces(state);

            // The behaviour of the block the cursor started on decides the delays.
            this.DecayDelay = behaviour == Behaviour.Default ? Math.Max(this.DecayDelay - 1, 0) : 1;
            this.updateDelay = 1;
        }

        public NbtCompound Save()
        {
            var tag = new NbtCompound
            {
                new NbtArray<int>("pos", [this.Position.X, this.Position.Y, this.Position.Z])
            };

            // Vanilla's codec leaves out what equals its default.
            if (this.Charge != 0)
                tag.Add(new NbtTag<int>("charge", this.Charge));

            if (this.DecayDelay != 1)
                tag.Add(new NbtTag<int>("decay_delay", this.DecayDelay));

            if (this.updateDelay != 0)
                tag.Add(new NbtTag<int>("update_delay", this.updateDelay));

            if (this.Facings is int faces)
            {
                var facings = new NbtList(NbtTagType.String, "facings");
                foreach (var face in FeatureHelpers.Directions)
                {
                    if ((faces >> (int)face & 1) != 0)
                        facings.Add(new NbtTag<string>(null, FeatureHelpers.FaceName(face)));
                }

                tag.Add(facings);
            }

            return tag;
        }

        public static Cursor? Load(NbtCompound tag)
        {
            if (!tag.TryGetTag("pos", out var pos) || pos is not NbtArray<int> { Count: 3 } coordinates)
                return null;

            var charge = tag.TryGetNumber<int>("charge", out var savedCharge) ? savedCharge : 0;
            var cursor = new Cursor(new Vector(coordinates[0], coordinates[1], coordinates[2]),
                Math.Clamp(charge, 0, MaxCharge));

            cursor.DecayDelay = tag.TryGetNumber<int>("decay_delay", out var decay) ? Math.Clamp(decay, 0, 1) : 1;
            cursor.updateDelay = tag.TryGetNumber<int>("update_delay", out var update) ? Math.Max(update, 0) : 0;

            if (tag.TryGetTag("facings", out var facings) && facings is NbtList names)
            {
                var faces = 0;
                foreach (var name in names)
                {
                    if (name is NbtTag<string> { Value: string text } && FaceNamed(text) is BlockFace face)
                        faces |= 1 << (int)face;
                }

                cursor.Facings = faces;
            }

            return cursor;
        }

        private static BlockFace? FaceNamed(string name)
        {
            foreach (var face in FeatureHelpers.Directions)
            {
                if (FeatureHelpers.FaceName(face) == name)
                    return face;
            }

            return null;
        }

        // The SculkBehaviour's attemptUseCharge.
        private int UseCharge(Behaviour behaviour, SculkLevel level, Vector origin, IRandomSource random) =>
            behaviour switch
            {
                Behaviour.Sculk => this.SculkUseCharge(level, origin, random),
                Behaviour.Vein when AttemptPlaceSculk(level, this.Position, random) => this.Charge - 1,
                Behaviour.Vein => random.NextInt(ChargeDecayRate) == 0 ? Mth.Floor(this.Charge * 0.5f) : this.Charge,
                _ => this.DecayDelay > 0 ? this.Charge : 0
            };

        // Vanilla's SculkBlock.attemptUseCharge: away from the catalyst, sculk grows a sensor or shrieker now and then,
        // and charge decays faster the further it is.
        private int SculkUseCharge(SculkLevel level, Vector origin, IRandomSource random)
        {
            var charge = this.Charge;
            if (charge == 0 || random.NextInt(ChargeDecayRate) != 0)
                return charge;

            var near = DistanceSquared(this.Position, origin) < NoGrowthRadius * NoGrowthRadius;
            if (!near && CanPlaceGrowth(level, this.Position))
            {
                if (random.NextInt(GrowthSpawnCost) < charge)
                {
                    var above = this.Position + Vector.Up;
                    var growth = GrowthState(level, above, random);
                    level.SetBlock(above, growth);
                    var sound = growth.Is(BlockIds.SculkShrieker)
                        ? "minecraft:block.sculk_shrieker.place"
                        : "minecraft:block.sculk_sensor.place";
                    level.PlaySound(this.Position, sound);
                }

                return Math.Max(0, charge - GrowthSpawnCost);
            }

            if (random.NextInt(AdditionalDecayRate) != 0)
                return charge;

            return charge - (near ? 1 : DecayPenalty(this.Position, origin, charge));
        }

        // Vanilla's getDecayPenalty, in its float arithmetic.
        private static int DecayPenalty(Vector position, Vector origin, int charge)
        {
            var outer = (float)Math.Sqrt(DistanceSquared(position, origin)) - NoGrowthRadius;
            var outerSquared = outer * outer;
            var reachSquared = (MaxGrowthRadius - NoGrowthRadius) * (MaxGrowthRadius - NoGrowthRadius);
            var factor = Math.Min(1.0f, outerSquared / reachSquared);
            return Math.Max(1, (int)(charge * factor * 0.5f));
        }

        // Vanilla's getRandomGrowthState: a shrieker (that can't summon) one time in 11, else a sensor; waterlogged in
        // a fluid.
        private static IBlock GrowthState(SculkLevel level, Vector position, IRandomSource random)
        {
            var growth = random.NextInt(ShriekerRate) == 0
                ? BlocksRegistry.SculkShrieker.WithProperty("can_summon", false)
                : BlocksRegistry.SculkSensor;

            return level.GetBlock(position).HasFluid() ? growth.WithProperty("waterlogged", true) : growth;
        }

        // Vanilla's canPlaceGrowth: air or water above, and at most two sensors or shriekers in the 9x3x9 box above.
        private static bool CanPlaceGrowth(SculkLevel level, Vector position)
        {
            var above = level.GetBlock(position + Vector.Up);
            if (!above.IsAir && !(above.Is(BlockIds.Water) && above.GetFluid() == FluidKind.Water))
                return false;

            var growths = 0;
            for (var y = position.Y; y <= position.Y + 2; y++)
            {
                for (var z = position.Z - 4; z <= position.Z + 4; z++)
                {
                    for (var x = position.X - 4; x <= position.X + 4; x++)
                    {
                        if (growthInhibitors.Contains(level.GetBlock(new Vector(x, y, z))) && ++growths > 2)
                            return false;
                    }
                }
            }

            return true;
        }

        // Vanilla's getValidMovementPos: a shuffled neighbour that's sculk or a vein the charge can reach, preferring
        // a vein on a block sculk can replace.
        private static Vector? MovementTarget(SculkLevel level, Vector position, IRandomSource random)
        {
            Span<Vector> offsets = stackalloc Vector[nonCornerNeighbours.Length];
            nonCornerNeighbours.CopyTo(offsets);
            FeatureHelpers.Shuffle(offsets, random);

            var target = position;
            foreach (var offset in offsets)
            {
                var candidate = position + offset;
                var block = level.GetBlock(candidate);
                if (BehaviourOf(block) == Behaviour.Default || !IsMovementUnobstructed(level, position, candidate))
                    continue;

                target = candidate;
                if (HasSubstrateAccess(level, block, candidate))
                    break;
            }

            return target == position ? null : target;
        }

        private static bool IsMovementUnobstructed(SculkLevel level, Vector from, Vector to)
        {
            if (Math.Abs(to.X - from.X) + Math.Abs(to.Y - from.Y) + Math.Abs(to.Z - from.Z) == 1)
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

        private static bool IsUnobstructed(SculkLevel level, Vector position, BlockFace face) =>
            !level.GetBlock(position.Offset(face)).IsFaceSturdy(face.Opposite());

        private static double DistanceSquared(Vector a, Vector b)
        {
            double dx = a.X - b.X;
            double dy = a.Y - b.Y;
            double dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }
    }

    private enum Behaviour
    {
        Default,
        Sculk,
        Vein
    }

    // The block's SculkBehaviour: sculk's, a vein's, or the default for every other block.
    private static Behaviour BehaviourOf(IBlock block) => block.RegistryId switch
    {
        BlockIds.Sculk => Behaviour.Sculk,
        BlockIds.SculkVein => Behaviour.Vein,
        _ => Behaviour.Default
    };

    // SculkBehaviour.attemptSpreadVein: sculk and veins spread veins around them; elsewhere a cursor that never stood
    // on sculk spreads veins in its own space, one that did grows back the veins it had, and one whose sculk had no
    // faces spreads around.
    private static bool AttemptSpreadVein(Behaviour behaviour, SculkLevel level, Vector position, IBlock state,
        int? facings)
    {
        if (behaviour != Behaviour.Default)
            return level.Spread(MultifaceSpreader.SculkVein, state, position);

        if (facings is not int faces)
            return level.Spread(MultifaceSpreader.SculkVeinSameSpace, level.GetBlock(position), position);

        if (faces == 0)
            return level.Spread(MultifaceSpreader.SculkVein, state, position);

        if (!state.IsAir && state.GetFluid() != FluidKind.Water)
            return false;

        return Regrow(level, position, state, faces);
    }

    // Vanilla's SculkVeinBlock.regrow: a vein on every listed face that still has support.
    private static bool Regrow(SculkLevel level, Vector position, IBlock state, int faces)
    {
        var vein = BlocksRegistry.SculkVein;
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

    // Vanilla's SculkVeinBlock.attemptPlaceSculk: a block sculk can replace behind one of the vein's faces, tried in a
    // shuffled order, turns into sculk, which spreads veins around it and takes in the veins beside it.
    private static bool AttemptPlaceSculk(SculkLevel level, Vector position, IRandomSource random)
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
            var replaced = level.GetBlock(target);
            if (!replaceable.Contains(replaced))
                continue;

            level.LiftEntities(target, replaced);
            level.SetBlock(target, BlocksRegistry.Sculk);
            level.PlaySound(target, "minecraft:block.sculk.spread");
            level.Spread(MultifaceSpreader.SculkVein, BlocksRegistry.Sculk, target);

            var back = face.Opposite();
            foreach (var direction in FeatureHelpers.Directions)
            {
                if (direction == back)
                    continue;

                var neighbour = target.Offset(direction);
                var block = level.GetBlock(neighbour);
                if (block.Is(BlockIds.SculkVein))
                    OnDischarged(Behaviour.Vein, level, block, neighbour);
            }

            return true;
        }

        return false;
    }

    // Vanilla's SculkVeinBlock.onDischarged: the vein (as last read) drops its faces now covered by sculk, and becomes
    // air or water without any.
    private static void OnDischarged(Behaviour behaviour, SculkLevel level, IBlock state, Vector position)
    {
        if (behaviour != Behaviour.Vein || !state.Is(BlockIds.SculkVein))
            return;

        foreach (var face in FeatureHelpers.Directions)
        {
            if (MultifaceSpreader.HasFace(state, face) && level.GetBlock(position.Offset(face)).Is(BlockIds.Sculk))
                state = state.WithProperty(FeatureHelpers.FaceName(face), false);
        }

        if (MultifaceSpreader.Faces(state) == 0)
            state = level.GetBlock(position).HasFluid() ? BlocksRegistry.Water : BlocksRegistry.Air;

        level.SetBlock(position, state);
    }

    // Vanilla's SculkVeinBlock.hasSubstrateAccess: a vein with a face on a block sculk can replace.
    private static bool HasSubstrateAccess(SculkLevel level, IBlock block, Vector position)
    {
        if (!block.Is(BlockIds.SculkVein))
            return false;

        foreach (var face in FeatureHelpers.Directions)
        {
            if (MultifaceSpreader.HasFace(block, face) && replaceable.Contains(level.GetBlock(position.Offset(face))))
                return true;
        }

        return false;
    }

    /// <summary>
    /// The live level as the charge sees it: writes go through the level's block updates, with vanilla's
    /// <c>setBlockAndUpdate</c> flags, and veins that spread with <c>MultifaceSpreader</c>'s flags (clients only).
    /// </summary>
    internal sealed class SculkLevel(AbstractLevel level) : IWorldGenLevel
    {
        private BlockUpdateFlags flags = BlockUpdateFlags.All;

        public long Seed => level.LevelData.RandomSeed;

        public int MinY => level.MinY;

        public int Height => level.Height;

        public int SeaLevel => 63;

        public IRandomSource Random => level.GrowthRandom;

        public IBlock GetBlock(Vector position) => level.BlockUpdates.GetBlock(position);

        // Vanilla's setBlock changes nothing, and runs no updates, where the block is already there.
        public bool SetBlock(Vector position, IBlock block)
        {
            if (level.BlockUpdates.GetBlock(position).IsSameState(block))
                return false;

            return level.BlockUpdates.SetBlock(position, block, this.flags);
        }

        // Vanilla's MultifaceSpreader places each vein with UPDATE_CLIENTS.
        public bool Spread(MultifaceSpreader spreader, IBlock state, Vector position)
        {
            this.flags = BlockUpdateFlags.Clients;
            try
            {
                return spreader.SpreadAll(state, this, position) > 0;
            }
            finally
            {
                this.flags = BlockUpdateFlags.All;
            }
        }

        public void PlaySound(Vector position, string sound) => BlockLighting.Play(level, position, sound);

        // Vanilla's Block.pushEntitiesUp, for blocks whose collision is a flat floor lower than a full block (mud,
        // soul sand): the entities standing in what becomes solid rise with it.
        public void LiftEntities(Vector position, IBlock replaced)
        {
            var boxes = BlockCollisionShapes.Get(level, position, replaced);
            if (boxes.Count != 1)
                return;

            // Collision boxes are relative to the block.
            var box = boxes[0];
            var flat = box.Min.X == 0 && box.Min.Z == 0 && box.Max.X == 1 && box.Max.Z == 1 && box.Min.Y == 0;
            if (flat)
                level.LiftEntitiesForSurfaceRise(position, box.Max.Y, 1);
        }

        public bool EnsureCanWrite(Vector position) => !level.IsOutsideBuildHeight(position.Y);

        public int GetHeight(HeightmapType type, int x, int z) => throw new NotSupportedException();

        public BiomeCodec GetBiome(Vector position) => throw new NotSupportedException();

        public void SetBlockEntity(Vector position, IBlockEntity blockEntity) => throw new NotSupportedException();

        public IBlockEntity? GetBlockEntity(Vector position) => level.GetResidentBlockEntity(position);

        public void AddEntity(GeneratedEntity entity) => throw new NotSupportedException();

        // Only world generation marks its veins for post-processing.
        public void MarkForPostProcessing(Vector position)
        {
        }

        public void ScheduleFluidTick(Vector position) =>
            level.Fluids.OnBlockChanged(position, level.BlockUpdates.GetBlock(position));
    }

    /// <summary>
    /// The order vanilla's <c>Object2IntOpenHashMap</c> of positions to charge (fastutil's, 16 expected entries at a
    /// load factor of 0.75) gives its entries: each position's slot from its hash (<c>Vec3i.hashCode</c>, mixed by
    /// <c>HashCommon.mix</c>) with linear probing, the table doubled and refilled once it holds more than three
    /// quarters, and the slots read from the last down. The charge particles' level events follow it.
    /// </summary>
    private sealed class ChargeTable
    {
        private Vector?[] slots = new Vector?[32];
        private readonly Dictionary<Vector, int> charges = [];

        public void Add(Vector position, int charge)
        {
            if (this.charges.TryGetValue(position, out var existing))
            {
                this.charges[position] = existing + charge;
                return;
            }

            this.charges[position] = charge;
            Insert(this.slots, position);

            // fastutil's maxFill: rehash once the map held three quarters (24 of 32) before this entry.
            if (this.charges.Count - 1 >= MaxFill(this.slots.Length))
                this.Rehash();
        }

        public IEnumerable<(Vector Position, int Charge)> InVanillaOrder()
        {
            for (var slot = this.slots.Length - 1; slot >= 0; slot--)
            {
                if (this.slots[slot] is Vector position)
                    yield return (position, this.charges[position]);
            }
        }

        // fastutil's rehash: the old slots, last first, into a table twice the size.
        private void Rehash()
        {
            var size = 2;
            while (size < Math.Ceiling((this.charges.Count + 1) / 0.75))
                size *= 2;

            var rehashed = new Vector?[size];
            for (var slot = this.slots.Length - 1; slot >= 0; slot--)
            {
                if (this.slots[slot] is Vector position)
                    Insert(rehashed, position);
            }

            this.slots = rehashed;
        }

        private static void Insert(Vector?[] slots, Vector position)
        {
            var mask = slots.Length - 1;
            var slot = Mix(HashCode(position)) & mask;
            while (slots[slot] is not null)
                slot = (slot + 1) & mask;

            slots[slot] = position;
        }

        private static int MaxFill(int size) => Math.Min((int)Math.Ceiling(size * 0.75), size - 1);

        // Vanilla's Vec3i.hashCode, in int arithmetic.
        private static int HashCode(Vector position) =>
            unchecked((position.Y + position.Z * 31) * 31 + position.X);

        // fastutil's HashCommon.mix.
        private static int Mix(int x)
        {
            var h = unchecked(x * -1640531527);
            return h ^ (int)((uint)h >> 16);
        }
    }
}
