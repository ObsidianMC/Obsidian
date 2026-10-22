using Obsidian.API.AI;

namespace Obsidian.Entities.AI;

public class Navigator : INavigator
{
    private readonly PathfinderMob? mob;
    private PathFinder? pathFinder;
    private MobPath? path;
    private int nextNode;
    private int repathDelay;
    private int stuckTicks;
    private VectorD lastProgressPosition;
    private VectorD lastPathTarget;
    private bool requested;
    private bool pathUsesVolume;

    // A path a goal found this tick (see FindPath), kept until the navigator ticks.
    private PreparedPath? preparedPath;

    public float SpeedModifier { get; set; } = 1;
    public bool CanOpenDoors { get; set; }
    public bool ReachedTarget { get; private set; }

    public Navigator() { }

    internal Navigator(PathfinderMob mob) => this.mob = mob;

    public bool IsNavigating { get; set; }

    public bool IsPaused { get; set; }

    public TargetType TargetType { get; set; }

    public VectorD TargetLocation { get; set; }
    public IEntity? Target { get; set; }

    public void NavigateTo(VectorD to)
    {
        if (!double.IsFinite(to.X) || !double.IsFinite(to.Y) || !double.IsFinite(to.Z))
            throw new ArgumentOutOfRangeException(nameof(to));

        if (!requested || TargetType != TargetType.Location || (TargetLocation - to).MagnitudeSquared() > 1)
            repathDelay = 0;
        TargetType = TargetType.Location;
        ReachedTarget = false;
        Target = null;
        TargetLocation = to;
        requested = true;
        IsNavigating = true;
    }

    public void NavigateTo(IEntity to)
    {
        ArgumentNullException.ThrowIfNull(to);
        if (!requested || !ReferenceEquals(Target, to))
            repathDelay = 0;
        TargetType = TargetType.Entity;
        ReachedTarget = false;
        Target = to;
        TargetLocation = to.Position;
        requested = true;
        IsNavigating = true;
    }

    public void Stop()
    {
        requested = false;
        IsNavigating = false;
        path = null;
        nextNode = 0;
        stuckTicks = 0;
        mob?.MoveControl.Stop();
    }

    // Plugin eviction must not reach a custom MoveControl.Stop override.
    internal void ClearForUnload()
    {
        Target = null;
        requested = false;
        IsNavigating = false;
        path = null;
        nextNode = 0;
        stuckTicks = 0;
        mob?.MoveControl.ClearForUnload();
    }

    /// <summary>
    /// Finds a path from the mob to <paramref name="to"/> for a goal deciding whether it can start, like vanilla's
    /// <c>createPath</c> in <c>canUse</c>. If the goal starts navigating there, this tick's navigation uses the path rather
    /// than finding it again.
    /// </summary>
    /// <returns>Whether the navigator could look: false for one that isn't a mob's.</returns>
    internal bool TryFindPath(VectorD to, out MobPath? found)
    {
        found = null;
        if (mob == null)
            return false;

        pathFinder ??= new PathFinder(mob);
        found = pathFinder.FindPath(to);
        preparedPath = new PreparedPath(found, mob.Position, to, VolumeMovement.UsesVolume(mob));
        return true;
    }

    internal void Tick()
    {
        // A prepared path only stands in for one found from the same place to the same target in the same tick.
        var prepared = preparedPath;
        preparedPath = null;

        if (mob == null || IsPaused || !requested)
        {
            if (mob != null && IsPaused)
                mob.MoveControl.Stop();
            return;
        }

        if (Target != null)
        {
            if (Target.Level != mob.Level || Target.Health <= 0)
            {
                Stop();
                return;
            }
            TargetLocation = Target.Position;
        }

        if ((TargetLocation - mob.Position).MagnitudeSquared() < 0.25f)
        {
            ReachedTarget = true;
            IsNavigating = false;
            mob.MoveControl.Stop();
            return;
        }

        var usesVolume = VolumeMovement.UsesVolume(mob);
        if (path != null && pathUsesVolume != usesVolume)
        {
            path = null;
            repathDelay = 0;
        }

        if (repathDelay > 0)
            repathDelay--;

        if (repathDelay == 0 && (path == null || (lastPathTarget - TargetLocation).MagnitudeSquared() > 1))
        {
            pathFinder ??= new PathFinder(mob);
            path = prepared is { } reused && reused.From == mob.Position && reused.To == TargetLocation
                && reused.UsesVolume == usesVolume
                ? reused.Path
                : pathFinder.FindPath(TargetLocation);
            pathUsesVolume = usesVolume;
            ReachedTarget = path?.ReachedTarget == true;
            nextNode = 0;
            lastPathTarget = TargetLocation;
            lastProgressPosition = mob.Position;
            stuckTicks = 0;
            repathDelay = 10 + mob.Random.Next(10);
        }

        if (path == null)
        {
            IsNavigating = false;
            mob.MoveControl.Stop();
            return;
        }

        // Vanilla's maxDistanceToWaypoint: narrow mobs count a node as reached from further away, so they don't circle it.
        var tolerance = mob.Dimension.Width > 0.75f ? mob.Dimension.Width / 2 : 0.75f - mob.Dimension.Width / 2;
        while (nextNode < path.Nodes.Count)
        {
            var difference = path.Nodes[nextNode] - mob.Position;
            if (difference.X * difference.X + difference.Z * difference.Z > tolerance * tolerance ||
                (usesVolume || !mob.InWater) && Math.Abs(difference.Y) > 0.5f)
                break;
            nextNode++;
        }

        if (nextNode >= path.Nodes.Count)
        {
            var partial = !path.ReachedTarget;
            Stop();
            if (partial)
                repathDelay = 20;
            return;
        }

        if ((mob.Position - lastProgressPosition).MagnitudeSquared() > 0.01f)
        {
            lastProgressPosition = mob.Position;
            stuckTicks = 0;
        }
        else if (++stuckTicks >= 100)
        {
            Stop();
            return;
        }

        IsNavigating = true;
        mob.MoveControl.MoveTo(path.Nodes[nextNode], SpeedModifier);
    }

    private sealed record PreparedPath(MobPath? Path, VectorD From, VectorD To, bool UsesVolume);
}
