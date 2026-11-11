using System.Threading;

namespace Obsidian.Entities;

public partial class Entity
{
    // The fall and its impact support, retained until the entity contact pass dispatches it.
    private BlockLanding? landedFall;

    internal virtual double ContactFallDistance { get; set; }

    internal bool ContactBounced { get; set; }

    internal virtual int ContactFireTicks { get; set; }

    internal bool IsContactBurning => (this.ContactFireTicks > 0 || this.Burning)
        && this is not Living { IsFireImmune: true };

    /// <summary>The entity random used for contact effects; mobs share their ordinary AI random.</summary>
    internal Random ContactEffectRandom { get; set; } = Random.Shared;

    internal virtual Random ContactRandom => this.ContactEffectRandom;

    private bool contactExtinguishPending;

    private List<(WorldData.IContactLevel Level, Vector Position, IBlock State)>? burningSnow;

    internal void RecordBurningSnow(WorldData.IContactLevel level, Vector position, IBlock state)
    {
        this.burningSnow ??= [];
        this.burningSnow.Add((level, position, state));
    }

    internal void RequestContactExtinguish() => this.contactExtinguishPending = true;

    internal void FinishInsideContact()
    {
        if (!this.contactExtinguishPending)
            return;

        this.contactExtinguishPending = false;
        if (this.burningSnow is not null)
        {
            foreach (var (level, position, state) in this.burningSnow)
                WorldData.PowderSnowContactBehavior.Melt(level, position, state, this);

            this.burningSnow.Clear();
        }

        this.ExtinguishFromContact();
    }

    private void ExtinguishFromContact()
    {
        if (this.IsContactBurning)
        {
            // Vanilla draws two entity floats for the extinguish pitch, even when the entity is silent.
            var pitch = 1.6f + (this.ContactRandom.NextSingle() - this.ContactRandom.NextSingle()) * 0.4f;
            if (this.Level is WorldData.AbstractLevel world)
            {
                var category = this switch
                {
                    Mob mob => mob.ContactSoundCategory,
                    IPlayer => SoundCategory.Players,
                    _ => SoundCategory.Ambient
                };
                WorldData.LevelSounds.Play(world, this.Position, "minecraft:entity.generic.extinguish_fire",
                    category, 0.7f, pitch);
            }
        }

        this.ContactFireTicks = 0;
        if (this.Burning)
        {
            this.Burning = false;
            this.PacketBroadcaster.QueuePacketToLevelInRange(this.Level, this.Position,
                new Net.Packets.Play.Clientbound.SetEntityDataPacket { EntityId = this.EntityId, Entity = this });
        }
    }

    /// <summary>
    /// Where the entity was when its last tick's movement began, like vanilla's <c>oldPosition</c>: set by its level
    /// before the tick of an entity the server moves, and after the contacts of a player, whose client moves them
    /// between ticks. Null until it's first set.
    /// </summary>
    internal VectorD? TickStartPosition { get; set; }

    /// <summary>
    /// How far the entity moved since <see cref="TickStartPosition"/>, which blocks such as sweet berry bushes read.
    /// </summary>
    internal VectorD TickMovement => this.Position - (this.TickStartPosition ?? this.Position);

    // What the entity's next move is scaled by, set by the blocks it's stuck in (see MakeStuckInBlock).
    private VectorD stuckMultiplier;

    /// <summary>
    /// Slows the entity's next move by <paramref name="multiplier"/> on each axis, as being inside a cobweb, a sweet
    /// berry bush or powder snow does, and ends its fall, like vanilla's <c>makeStuckInBlock</c>. The block sets it
    /// again each tick the entity is still inside.
    /// </summary>
    internal void MakeStuckInBlock(VectorD multiplier)
    {
        this.ResetFallDistance();
        this.stuckMultiplier = multiplier;
    }

    /// <summary>
    /// The multiplier <see cref="MakeStuckInBlock"/> set since the last move, cleared as vanilla's <c>Entity.move</c>
    /// clears it, or null when there's none.
    /// </summary>
    internal VectorD? TakeStuckMultiplier()
    {
        var multiplier = this.stuckMultiplier;
        if (multiplier.MagnitudeSquared() <= 1.0E-7)
            return null;

        this.stuckMultiplier = VectorD.Zero;
        return multiplier;
    }

    internal void TrackContactFall(
        double descent, bool grounded, VectorD impactPosition, (Vector, IBlock)? support)
    {
        // Bat.checkFallDamage intentionally does nothing, even when externally moved.
        if (this is Bat)
            return;

        if (descent < 0)
            this.ContactFallDistance -= (float)descent;

        if (!grounded)
            return;

        this.RecordLanding(this.ContactFallDistance, impactPosition, support);
        this.ContactFallDistance = 0;
    }

    internal virtual ValueTask CauseContactFallAsync(
        double distance, float multiplier = 1, VectorD? impact = null) => default;

    /// <summary>
    /// Records that the entity landed after falling <paramref name="fallDistance"/> blocks, which the entity contact
    /// pass (<see cref="WorldData.EntityContactPass"/>) reports to the block it landed on after the entity's tick, as
    /// vanilla's <c>checkFallDamage</c> calls <c>fallOn</c>. A landing without a fall isn't one.
    /// </summary>
    internal void RecordLanding(
        double fallDistance, VectorD? impact = null, (Vector, IBlock)? support = null)
    {
        if (fallDistance <= 0)
            return;

        var at = impact ?? this.Position;
        if (support is null && this.Level is WorldData.IContactLevel level)
            support = WorldData.EntityContactPass.SupportAt(level, this, at);

        var cell = support?.Item1 ?? (Vector)(at - new VectorD(0, 0.2, 0)).Floor();
        Volatile.Write(ref this.landedFall, new BlockLanding(fallDistance, at, cell, support?.Item2));
    }

    /// <summary>
    /// The entity's box where it is now: its dimensions around its position. <see cref="BoundingBox"/> follows the
    /// position only where a mover updates it, so an entity placed or teleported by setting its position keeps an old
    /// one; what looks for entities by where they are (pressure plates, tripwires) reads this instead.
    /// </summary>
    internal virtual BoundingBox CurrentBox =>
        this.Dimension == EntityDimension.Zero ? this.BoundingBox : this.Dimension.CreateBBFromPosition(this.Position);

    /// <summary>The landing recorded by <see cref="RecordLanding"/> since the last call, or null.</summary>
    internal BlockLanding? TakeLanding() => Interlocked.Exchange(ref this.landedFall, null);
}

/// <summary>The support and entity position at impact, retained across later movement before dispatch.</summary>
internal sealed record BlockLanding(double Distance, VectorD Impact, Vector BlockPosition, IBlock? State);
