

namespace Obsidian.Entities;

public class Animal : AgeableMob
{
    public async override ValueTask TickAsync()
    {
        // TODO obby doesn't properly spawn entities yet
        var closest = Level.PlayersInRange((Vector)Position).MinBy(p => VectorD.Distance(Position, p.Position));
        if (closest is not null)
        {
            var closestPosition = new VectorD(closest.Position.X, closest.HeadY, closest.Position.Z);

            var lookAt = closestPosition - Position;

            var yaw = (byte)((Math.Atan2(lookAt.Z, lookAt.X) * (256 / (2 * Math.PI)) - 64) % 256);
            var pitch = (byte)(256 - (Math.Asin(lookAt.Y / lookAt.Magnitude) * (256 / (2 * Math.PI))));

            SetRotation(new Angle(yaw), new Angle(pitch), MovementFlags.OnGround);
            SetHeadRotation(new Angle(yaw));
        }

        await base.TickAsync();
    }
}
