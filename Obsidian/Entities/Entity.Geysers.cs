namespace Obsidian.Entities;

public partial class Entity
{
    // Vanilla checks accumulated fall distance before a geyser adds motion, even when it cannot launch the entity.
    internal static float GeyserFallDistance(float distance, double motionY) =>
        motionY > -0.5 && distance > 1 ? 1 : distance;

    internal virtual void CheckGeyserFallDistance()
    {
    }
}

public partial class Mob
{
    internal override void CheckGeyserFallDistance() =>
        this.fallDistance = GeyserFallDistance(this.fallDistance, this.Motion.Y);
}

public partial class PlayerBase
{
    internal override void CheckGeyserFallDistance() =>
        this.FallDistance = GeyserFallDistance(this.FallDistance, this.Motion.Y);
}
