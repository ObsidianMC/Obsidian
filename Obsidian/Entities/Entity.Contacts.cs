using System.Threading;

namespace Obsidian.Entities;

public partial class Entity
{
    // The fall and its impact support, retained until the entity contact pass dispatches it.
    private BlockLanding? landedFall;

    internal virtual double ContactFallDistance { get; set; }

    internal bool ContactBounced { get; set; }

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
