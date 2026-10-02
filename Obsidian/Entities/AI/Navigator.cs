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
    private VectorF lastProgressPosition;
    private VectorF lastPathTarget;
    private bool requested;

    public float SpeedModifier { get; set; } = 1;
    public bool CanOpenDoors { get; set; }
    public bool ReachedTarget { get; private set; }

    public Navigator() { }

    internal Navigator(PathfinderMob mob) => this.mob = mob;

    public bool IsNavigating { get; set; }

    public bool IsPaused { get; set; }

    public TargetType TargetType { get; set; }

    public VectorF TargetLocation { get; set; }
    public IEntity? Target { get; set; }

    public void NavigateTo(VectorF to)
    {
        if (!float.IsFinite(to.X) || !float.IsFinite(to.Y) || !float.IsFinite(to.Z))
            throw new ArgumentOutOfRangeException(nameof(to));

        if (!requested || TargetType != TargetType.Location || (TargetLocation - to).MagnitudeSquared() > 1)
            repathDelay = 0;
        TargetType = TargetType.Location;
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

    internal void Tick()
    {
        if (mob == null || IsPaused || !requested)
        {
            if (mob != null && (IsPaused || !mob.MoveControl.IsStrafing))
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

        if (repathDelay > 0)
            repathDelay--;

        if (repathDelay == 0 && (path == null || (lastPathTarget - TargetLocation).MagnitudeSquared() > 1))
        {
            pathFinder ??= new PathFinder(mob);
            path = pathFinder.FindPath(TargetLocation);
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

        var tolerance = MathF.Max(0.2f, mob.Dimension.Width / 2);
        while (nextNode < path.Nodes.Count)
        {
            var difference = path.Nodes[nextNode] - mob.Position;
            if (difference.X * difference.X + difference.Z * difference.Z > tolerance * tolerance ||
                MathF.Abs(difference.Y) > 0.5f)
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
}
