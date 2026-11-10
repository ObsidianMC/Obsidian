namespace Obsidian.Entities;

public partial class Entity
{
    // The fall that ended in landing since the entity contact pass last looked, in blocks, or 0.
    private double landedFall;

    internal virtual double ContactFallDistance { get; set; }

    internal bool ContactBounced { get; set; }

    internal void TrackContactFall(double descent, bool grounded)
    {
        // Bat.checkFallDamage intentionally does nothing, even when externally moved.
        if (this is Bat)
            return;

        if (descent < 0)
            this.ContactFallDistance -= (float)descent;

        if (!grounded)
            return;

        this.RecordLanding(this.ContactFallDistance);
        this.ContactFallDistance = 0;
    }

    internal virtual ValueTask CauseContactFallAsync(double distance, float multiplier = 1) => default;

    /// <summary>
    /// Records that the entity landed after falling <paramref name="fallDistance"/> blocks, which the entity contact
    /// pass (<see cref="WorldData.EntityContactPass"/>) reports to the block it landed on after the entity's tick, as
    /// vanilla's <c>checkFallDamage</c> calls <c>fallOn</c>. A landing without a fall isn't one.
    /// </summary>
    internal void RecordLanding(double fallDistance)
    {
        if (fallDistance > 0)
            this.landedFall = fallDistance;
    }

    /// <summary>
    /// The entity's box where it is now: its dimensions around its position. <see cref="BoundingBox"/> follows the
    /// position only where a mover updates it, so an entity placed or teleported by setting its position keeps an old
    /// one; what looks for entities by where they are (pressure plates, tripwires) reads this instead.
    /// </summary>
    internal virtual BoundingBox CurrentBox =>
        this.Dimension == EntityDimension.Zero ? this.BoundingBox : this.Dimension.CreateBBFromPosition(this.Position);

    /// <summary>The fall recorded by <see cref="RecordLanding"/> since the last call, or 0.</summary>
    internal double TakeLanding()
    {
        var fall = this.landedFall;
        this.landedFall = 0;
        return fall;
    }
}
