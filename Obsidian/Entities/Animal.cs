

namespace Obsidian.Entities;

public class Animal : AgeableMob
{
    public async override ValueTask TickAsync()
    {
        // TODO obby doesn't properly spawn entities yet
        var players = World.PlayersInRange((Vector)Position);
        if (players.Any())
        {
            var closest = players.OrderBy(p => VectorF.Distance(Position, p.Position)).First();
            var closestPosition = new VectorF()
            {
                X = closest.Position.X,
                Y = (float)closest.HeadY,
                Z = closest.Position.Z
            };

            var lookAt = closestPosition - Position;

            var yaw = (byte)((MathF.Atan2(lookAt.Z, lookAt.X) * (256 / (2 * MathF.PI)) - 64) % 256);
            var pitch = (byte)(256 - (MathF.Asin(lookAt.Y / lookAt.Magnitude) * (256 / (2 * MathF.PI))));

            SetRotation(new Angle(yaw), new Angle(pitch), MovementFlags.OnGround);
            SetHeadRotation(new Angle(yaw));
        }

        await base.TickAsync();
    }
}
