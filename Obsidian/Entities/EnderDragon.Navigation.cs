using Obsidian.WorldData;
using Obsidian.API.World.Generator.RandomSources;

namespace Obsidian.Entities;

public sealed partial class EnderDragon
{
    // Vanilla's connections between the outer, middle and inner flight rings.
    private static readonly int[] flightConnections = [6146, 8197, 8202, 16404, 32808, 32848, 65696, 131392,
        131712, 263424, 526848, 525313, 1581057, 3166214, 2138120, 6373424, 4358208, 12910976,
        9044480, 9706496, 15216640, 13688832, 11763712, 8257536];
    private VectorD[]? flightNodes;
    private readonly Queue<VectorD> flightPath = [];
    private bool clockwise;
    private bool hasFlightDestination;

    private int CrystalCount => Level.GetEntitiesInRange(arena, 128).OfType<EndCrystal>().Count(item => item.Health > 0);

    private void EnsureFlightNodes()
    {
        if (flightNodes != null) return;
        flightNodes = new VectorD[24];
        for (var index = 0; index < flightNodes.Length; index++)
        {
            var (radius, count, offset, height) = index < 12 ? (60, 12, 0, 5) : index < 20 ? (40, 8, 12, 15) : (20, 4, 20, 5);
            var step = count switch { 12 => 0.2617994f, 8 => 0.3926991f, _ => 0.7853982f };
            var angle = 2f * (-MathF.PI + step * (index - offset));
            var x = (int)Math.Floor(Mth.Cos(angle) * radius);
            var z = (int)Math.Floor(Mth.Sin(angle) * radius);
            var surface = Level is AbstractLevel level && level.GetLoadedChunk(x >> 4, z >> 4) is { } chunk
                ? chunk.GetHeight(HeightmapType.MotionBlockingNoLeaves, x, z) : 0;
            flightNodes[index] = new VectorD(x, Math.Max(73, surface + height), z);
        }
    }

    private int NearestFlightNode(VectorD position)
    {
        EnsureFlightNodes();
        var first = CrystalCount > 0 ? 0 : 12;
        var nearest = first;
        var distance = double.MaxValue;
        for (var index = first; index < flightNodes!.Length; index++)
        {
            var candidate = (flightNodes[index] - position).MagnitudeSquared();
            if (candidate >= distance) continue;
            nearest = index;
            distance = candidate;
        }
        return nearest;
    }

    internal IReadOnlyList<VectorD> FindFlightPath(int start, int end)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(start);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(start, 24);
        ArgumentOutOfRangeException.ThrowIfNegative(end);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(end, 24);
        EnsureFlightNodes();
        var first = CrystalCount > 0 ? 0 : 12;
        var scores = Enumerable.Repeat(double.PositiveInfinity, 24).ToArray();
        var parents = Enumerable.Repeat(-1, 24).ToArray();
        var closed = new bool[24];
        var open = new PriorityQueue<int, double>();
        scores[start] = 0;
        open.Enqueue(start, 0);
        var closest = start;
        while (open.TryDequeue(out var current, out _))
        {
            if (closed[current]) continue;
            closed[current] = true;
            if ((flightNodes![current] - flightNodes[end]).MagnitudeSquared() < (flightNodes[closest] - flightNodes[end]).MagnitudeSquared()) closest = current;
            if (current == end) break;
            for (var next = first; next < 24; next++)
            {
                if (closed[next] || (flightConnections[current] & 1 << next) == 0) continue;
                var score = scores[current] + (flightNodes[current] - flightNodes[next]).Magnitude;
                if (score >= scores[next]) continue;
                scores[next] = score;
                parents[next] = current;
                open.Enqueue(next, score + (flightNodes[next] - flightNodes[end]).Magnitude);
            }
        }
        List<VectorD> path = [];
        for (var node = closest; node >= 0; node = parents[node]) path.Add(flightNodes![node]);
        path.Reverse();
        return path;
    }

    private void SetFlightPath(int end, VectorD? finalDestination = null)
    {
        flightPath.Clear();
        var path = FindFlightPath(NearestFlightNode(Position), end);
        // A one-node path still has to be flown, or the phase waits forever for a destination.
        foreach (var point in path.Count > 1 ? path.Skip(1) : path) flightPath.Enqueue(point);
        if (finalDestination is { } final) flightPath.Enqueue(final);
        AdvanceFlightPath();
    }

    private void AdvanceFlightPath()
    {
        if (!flightPath.TryDequeue(out var next)) return;
        destination = next + new VectorD(0, Random.NextSingle() * 20, 0);
        hasFlightDestination = true;
    }

    private void TickHolding()
    {
        var distance = (destination - Position).MagnitudeSquared();
        if (hasFlightDestination && distance >= 100 && distance <= 22500 && !MovementFlags.HasFlag(MovementFlags.HorizontalCollision)) return;
        if (flightPath.Count == 0)
        {
            if (hasFlightDestination)
            {
                var crystals = CrystalCount;
                if (Random.Next(crystals + 3) == 0) { SetPhase(DragonPhase.LandingApproach); return; }
                var target = Level.GetPlayersInRange(arena, 192).Where(IsValidTarget).MinBy(player => (player.Position - arena).MagnitudeSquared());
                if (target != null && (Random.Next((int)((target.Position - arena).MagnitudeSquared() / 512 + 2)) == 0 || Random.Next(crystals + 2) == 0))
                {
                    AttackTarget = target;
                    SetPhase(DragonPhase.Strafing);
                    return;
                }
            }
            var node = NearestFlightNode(Position);
            var next = node;
            if (Random.Next(8) == 0) { clockwise = !clockwise; next += 6; }
            next += clockwise ? 1 : -1;
            // An unbound dragon uses the middle ring; a fight dragon uses the outer ring.
            next = fightControlled ? (next % 12 + 12) % 12 : ((next - 12) & 7) + 12;
            flightPoint = next;
            SetFlightPath(next);
        }
        else AdvanceFlightPath();
    }

    private void TickLandingApproach()
    {
        if (!hasFlightDestination)
        {
            var player = Level.GetPlayersInRange(arena, 192).Where(IsValidTarget).MinBy(target => (target.Position - arena).MagnitudeSquared());
            var approach = new VectorD(40, arena.Y, 0);
            if (player != null)
            {
                var direction = new VectorD(player.Position.X, 0, player.Position.Z);
                if (direction.Magnitude > 0) direction /= direction.Magnitude;
                approach = direction * -40;
                approach.Y = 105;
            }
            SetFlightPath(NearestFlightNode(approach), Podium);
        }
        else if ((destination - Position).MagnitudeSquared() < 100 || (destination - Position).MagnitudeSquared() > 22500 || MovementFlags.HasFlag(MovementFlags.HorizontalCollision))
            AdvanceFlightPath();
        // Like vanilla, landing starts once the podium is the last node left, not after reaching it.
        if (phase == DragonPhase.LandingApproach && hasFlightDestination && flightPath.Count == 0) SetPhase(DragonPhase.Landing);
    }

    private void TickTakeoff()
    {
        if (!hasFlightDestination)
        {
            var point = DragonForward * -40;
            point.Y = 105;
            var node = NearestFlightNode(point);
            node = fightControlled && CrystalCount > 0 ? (node % 12 + 12) % 12 : ((node - 12) & 7) + 12;
            SetFlightPath(node);
        }
        else if ((Position - arena).MagnitudeSquared() > 100) SetPhase(DragonPhase.Holding);
    }

    private void TickStrafingPath(IEntity target)
    {
        var horizontal = new VectorD(target.Position.X - Position.X, 0, target.Position.Z - Position.Z).Magnitude;
        var targetPoint = target.Position + new VectorD(0, Math.Min(0.4 + horizontal / 80 - 1, 10), 0);
        if (!hasFlightDestination)
        {
            SetFlightPath(NearestFlightNode(target.Position), targetPoint.Floor());
            return;
        }
        if (flightPath.Count == 0) destination = targetPoint;
        var distance = (destination - Position).MagnitudeSquared();
        if (distance >= 100 && distance <= 22500) return;
        if (flightPath.Count > 0) AdvanceFlightPath();
        else
        {
            var next = NearestFlightNode(Position);
            if (Random.Next(8) == 0) { clockwise = !clockwise; next += 6; }
            next += clockwise ? 1 : -1;
            next = fightControlled && CrystalCount > 0 ? (next % 12 + 12) % 12 : ((next - 12) & 7) + 12;
            SetFlightPath(next);
        }
    }
}
