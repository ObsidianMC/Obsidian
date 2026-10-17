using Obsidian.Entities.AI;
using Obsidian.Net.Packets.Play.Clientbound;

namespace Obsidian.Entities;

/// <summary>
/// A move a client made to the vehicle it controls, as its <c>MoveVehicle</c> packet says.
/// </summary>
internal readonly record struct VehicleMove(VectorD Position, float Yaw, float Pitch, bool OnGround);

/// <summary>
/// Lets the controlling rider's client move a mob, like vanilla's player-controlled vehicles: the client simulates the
/// mob and sends where it went, and the server checks each move like vanilla's <c>handleMoveVehicle</c> before taking
/// it, putting the client's mob back where the server has it otherwise.
/// </summary>
/// <remarks>
/// <para>
/// Moves arrive on the connection's thread (<see cref="Enqueue"/>) and are checked on the mob's tick
/// (<see cref="Tick"/>), where its terrain and position are its own. A mob is client-controlled from the first move
/// its rider's client sends until that rider leaves; before that the server steers it from the rider's input, which is
/// what clients that don't predict the mob get.
/// </para>
/// <para>
/// The checks and their thresholds are vanilla's, since they decide which of a client's moves stand: a move from the
/// tick's first position longer than 10 blocks beyond the mob's motion is refused; so is one that ends more than a
/// quarter of a block from where the server's collision lets the mob go, unless the mob started inside blocks, and one
/// that ends in blocks it wasn't in. A walking mob kept floating for 80 ticks (longer under lower gravity) kicks its
/// rider. Like vanilla, the vertical part of the collision check is ignored.
/// </para>
/// </remarks>
internal sealed class ClientVehicleControl
{
    // Vanilla's limits: the squared distance a move may exceed the motion by, the squared horizontal distance it may
    // end from the server's collision, and the ticks a walking vehicle may float under vanilla's gravity.
    private const double TooQuicklySquared = 100;
    private const double WronglySquared = 0.0625;
    private const int FloatingTicks = 80;

    private readonly ConcurrentQueue<(PlayerBase Sender, VehicleMove Move)> moves = new();

    // The rider whose client moves the mob, from its first move.
    private PlayerBase? controller;

    // Where the last move the server took put the mob this tick, which the mob travels to.
    private VehicleMove? accepted;

    // Server collision support, kept separately from the client's on-ground flag.
    private bool collisionBelow;

    // Whether the last move taken left the mob floating, and for how many ticks it has.
    private bool floating;
    private int floatingTicks;

    /// <summary>
    /// Whether <paramref name="rider"/>'s client moves the mob, rather than the server steering it.
    /// </summary>
    public bool ControlledBy(PlayerBase? rider) => rider is not null && ReferenceEquals(this.controller, rider);

    /// <summary>
    /// Whether the controlling rider kept a walking mob floating for too long, for which vanilla kicks it.
    /// </summary>
    public bool FloatingTooLong { get; private set; }

    /// <summary>
    /// Queues a move a client sent, from any thread.
    /// </summary>
    public void Enqueue(PlayerBase sender, VehicleMove move) => this.moves.Enqueue((sender, move));

    /// <summary>
    /// Checks the moves <paramref name="rider"/>'s client sent since the last tick, on the mob's tick before it
    /// travels. Moves from anyone else, such as a rider that left, are dropped.
    /// </summary>
    /// <param name="vehicle">The mob.</param>
    /// <param name="rider">The mob's controlling passenger, or null.</param>
    /// <param name="flying">Whether the mob flies, which vanilla never counts as floating.</param>
    public void Tick(Mob vehicle, PlayerBase? rider, bool flying)
    {
        if (!ReferenceEquals(rider, this.controller))
        {
            this.Reset(rider);
            this.collisionBelow = vehicle.MovementFlags.HasFlag(MovementFlags.OnGround);
        }

        this.accepted = null;

        var firstGood = vehicle.Position;
        var position = firstGood;
        var onGround = vehicle.MovementFlags.HasFlag(MovementFlags.OnGround);

        while (this.moves.TryDequeue(out var queued))
        {
            if (rider is null || !ReferenceEquals(queued.Sender, rider))
                continue;

            this.controller = rider;

            if (!this.Check(vehicle, queued.Move, firstGood, position, onGround, flying))
            {
                rider.SendPacket(new MoveVehiclePacket
                {
                    Position = position,
                    Yaw = vehicle.Yaw.Degrees,
                    Pitch = vehicle.Pitch.Degrees
                });

                continue;
            }

            position = queued.Move.Position;
            onGround = queued.Move.OnGround;
            this.accepted = queued.Move;
        }

        if (this.controller is null)
            return;

        this.floatingTicks = this.floating ? this.floatingTicks + 1 : 0;
        this.FloatingTooLong = this.floatingTicks > MaximumFloatingTicks(vehicle);
    }

    /// <summary>
    /// Where the moves taken this tick put the mob, if any were: the mob goes there instead of travelling, takes the
    /// move's rotation, and stands on the ground if the client says so.
    /// </summary>
    public VehicleMove? Accepted => this.accepted;

    private void Reset(PlayerBase? rider)
    {
        this.controller = null;
        this.floating = false;
        this.floatingTicks = 0;
        this.FloatingTooLong = false;

        // A rider's moves wait for the tick that reads them, where others' are dropped; with no rider, none can count.
        if (rider is null)
            this.moves.Clear();
    }

    // Vanilla's handleMoveVehicle checks, from the position the moves taken so far this tick left the mob at.
    private bool Check(
        Mob vehicle,
        VehicleMove move,
        VectorD firstGood,
        VectorD from,
        bool wasOnGround,
        bool flying)
    {
        var target = move.Position;
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Y) || !double.IsFinite(target.Z)
            || !float.IsFinite(move.Yaw) || !float.IsFinite(move.Pitch)
            || Math.Abs(target.X) > 3.0E7 || Math.Abs(target.Z) > 3.0E7 || Math.Abs(target.Y) > 2.0E7)
        {
            return false;
        }

        var travelled = target - firstGood;
        if (travelled.MagnitudeSquared() - vehicle.Motion.MagnitudeSquared() > TooQuicklySquared)
            return false;

        var terrain = vehicle.Terrain;
        var bounds = vehicle.Dimension.CreateBBFromPosition(from);
        var delta = target - from;
        var stepHeight = Math.Clamp(vehicle.GetAttributeValue("minecraft:step_height"), 0, 10);
        var displacement = EntityMovement.Collide(terrain, bounds, delta, stepHeight, wasOnGround);
        var moved = from + displacement;
        var missX = target.X - moved.X;
        var missZ = target.Z - moved.Z;
        var wrong = missX * missX + missZ * missZ > WronglySquared;

        if (wrong && terrain.IsFree(bounds) || CollidesWithNew(vehicle, bounds, target))
            return false;

        // Vanilla checks the preceding move's verticalCollisionBelow, then Entity.move records downward collision.
        var wasSupported = this.collisionBelow;
        this.collisionBelow = delta.Y < 0 && displacement.Y != delta.Y;
        this.floating = delta.Y >= -0.03125
            && !wasSupported
            && !flying
            && !vehicle.NoGravity
            && NoBlocksAround(vehicle, target);

        return true;
    }

    // Whether the mob at the target overlaps blocks it didn't overlap where it was, like vanilla's
    // isEntityCollidingWithAnythingNew.
    private static bool CollidesWithNew(Mob vehicle, BoundingBox from, VectorD target)
    {
        var margin = new VectorD(1.0E-5, 1.0E-5, 1.0E-5);
        var old = new BoundingBox(from.Min + margin, from.Max - margin);
        var bounds = vehicle.Dimension.CreateBBFromPosition(target);
        var moved = new BoundingBox(bounds.Min + margin, bounds.Max - margin);

        return vehicle.Terrain
            .GetCollisions(moved)
            .Any(shape => MobTerrain.Overlaps(shape, moved) && !MobTerrain.Overlaps(shape, old));
    }

    // Whether only air surrounds the mob at the target, down to half a block below it, like vanilla's noBlocksAround.
    private static bool NoBlocksAround(Mob vehicle, VectorD target)
    {
        var bounds = vehicle.Dimension.CreateBBFromPosition(target);
        var min = (bounds.Min - new VectorD(0.0625, 0.6125, 0.0625)).Floor();
        var max = (bounds.Max + new VectorD(0.0625, 0.0625, 0.0625)).Floor();

        for (var x = (int)min.X; x <= (int)max.X; x++)
        {
            for (var y = (int)min.Y; y <= (int)max.Y; y++)
            {
                for (var z = (int)min.Z; z <= (int)max.Z; z++)
                {
                    if (vehicle.Terrain.GetBlock(new Vector(x, y, z)) is { IsAir: false })
                        return false;
                }
            }
        }

        return true;
    }

    // Vanilla's getMaximumFlyingTicks: 80 ticks under vanilla's gravity, longer under less, and no limit without.
    private static int MaximumFloatingTicks(Mob vehicle)
    {
        var gravity = vehicle.HasAttribute("minecraft:gravity")
            ? vehicle.GetAttributeValue("minecraft:gravity")
            : 0.08;
        if (gravity < 1.0E-5)
            return int.MaxValue;

        return (int)Math.Ceiling(FloatingTicks * Math.Max(0.08 / gravity, 1));
    }
}
