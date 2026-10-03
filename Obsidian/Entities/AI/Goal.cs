using Obsidian.API.AI;

namespace Obsidian.Entities.AI;

[Flags]
public enum GoalFlags
{
    None = 0,
    Move = 1,
    Look = 2,
    Jump = 4,
    Target = 8
}

public abstract class Goal : IGoal
{
    public abstract GoalFlags Flags { get; }
    public virtual bool IsInterruptible => true;
    public virtual bool RequiresUpdateEveryTick => false;
    public abstract bool CanUse();
    public virtual bool CanContinue() => CanUse();
    public virtual void Start() { }
    public virtual void Stop() { }
    public virtual ValueTask TickAsync() => default;

    // Preserve the synchronous plugin entry point; the simulation uses TickAsync.
    public void Run() => TickAsync().AsTask().GetAwaiter().GetResult();
}

public sealed class GoalSelector : IGoalController
{
    private readonly List<WrappedGoal> goals = [];
    private readonly List<WrappedGoal> conflicts = [];
    public bool IsPaused { get; private set; }
    public bool IsExecuting => goals.Any(goal => goal.Running);

    public void AddGoal(int priority, Goal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        if (goals.Any(entry => ReferenceEquals(entry.Goal, goal)))
            return;

        goals.Add(new WrappedGoal(priority, goal));
    }

    public void AddGoal(IGoal goal) => AddGoal(int.MaxValue, goal as Goal ?? new LegacyGoal(goal));

    public void RemoveGoal(IGoal goal)
    {
        foreach (var entry in goals.Where(entry => ReferenceEquals(entry.Goal, goal) ||
            entry.Goal is LegacyGoal legacy && ReferenceEquals(legacy.Inner, goal)).ToArray())
        {
            Stop(entry);
            goals.Remove(entry);
        }
    }

    public void Clear()
    {
        Cancel();
        goals.Clear();
    }

    public void Cancel()
    {
        foreach (var goal in goals)
            Stop(goal);
    }

    public void Pause()
    {
        Cancel();
        IsPaused = true;
    }

    public void Resume() => IsPaused = false;

    public async ValueTask TickAsync(bool updateSelection = true)
    {
        if (IsPaused)
            return;

        if (!updateSelection)
        {
            foreach (var entry in goals)
            {
                if (entry.Running && entry.Goal.RequiresUpdateEveryTick)
                    await entry.Goal.TickAsync();
            }
            return;
        }

        foreach (var entry in goals)
        {
            if (entry.Running && !entry.Goal.CanContinue())
                Stop(entry);
        }

        foreach (var entry in goals)
        {
            if (entry.Running)
                continue;

            conflicts.Clear();
            var blocked = false;
            foreach (var other in goals)
            {
                if (!other.Running || (other.Goal.Flags & entry.Goal.Flags) == 0)
                    continue;

                conflicts.Add(other);
                if (other.Priority <= entry.Priority || !other.Goal.IsInterruptible)
                    blocked = true;
            }
            if (blocked || !entry.Goal.CanUse())
                continue;

            foreach (var conflict in conflicts)
                Stop(conflict);

            entry.Running = true;
            entry.Goal.Start();
        }
        conflicts.Clear();

        foreach (var entry in goals)
        {
            if (entry.Running)
                await entry.Goal.TickAsync();
        }
    }

    private static void Stop(WrappedGoal entry)
    {
        if (!entry.Running)
            return;

        entry.Running = false;
        entry.Goal.Stop();
    }

    private sealed class WrappedGoal(int priority, Goal goal)
    {
        public int Priority { get; } = priority;
        public Goal Goal { get; } = goal;
        public bool Running { get; set; }
    }

    private sealed class LegacyGoal(IGoal inner) : Goal
    {
        public IGoal Inner { get; } = inner;
        public override bool RequiresUpdateEveryTick => true;
        public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look | GoalFlags.Jump;
        public override bool CanUse() => true;
        public override ValueTask TickAsync()
        {
            Inner.Run();
            return default;
        }
    }
}
