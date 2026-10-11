using Obsidian.API.AI;
using Obsidian.Plugins;
using Obsidian.Plugins.PluginProviders;
using System.Runtime.Loader;

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
        if (goals.Any(entry => ReferenceEquals(CodeOf(entry.Goal), goal)))
            return;

        goals.Add(new WrappedGoal(priority, PluginGoal.Wrap(goal)));
    }

    public void AddGoal(IGoal goal)
    {
        ArgumentNullException.ThrowIfNull(goal);
        AddGoal(int.MaxValue, goal as Goal ?? new LegacyGoal(goal));
    }

    /// <summary>
    /// Removes the goals whose code is in <paramref name="context"/>, an unloading plugin's: they'd keep it loaded.
    /// Their plugin is closed by then, so removing them doesn't call into it.
    /// </summary>
    internal void RemoveGoalsFrom(AssemblyLoadContext context)
    {
        foreach (var entry in goals.ToArray())
        {
            if (AssemblyLoadContext.GetLoadContext(CodeOf(entry.Goal).GetType().Assembly) != context)
                continue;

            entry.Running = false;
            goals.Remove(entry);
        }
    }

    public void RemoveGoal(IGoal goal)
    {
        foreach (var entry in goals.Where(entry => ReferenceEquals(CodeOf(entry.Goal), goal)).ToArray())
        {
            Stop(entry);
            goals.Remove(entry);
        }
    }

    // The goal as it was given: a plugin's goal or API goal is wrapped.
    private static object CodeOf(Goal goal)
    {
        var unwrapped = goal is PluginGoal plugin ? plugin.Inner : goal;
        return unwrapped is LegacyGoal legacy ? legacy.Inner : unwrapped;
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

    // A goal given through the API as just an IGoal, which runs every tick.
    private sealed class LegacyGoal(IGoal inner) : Goal
    {
        public IGoal Inner { get; } = inner;
        public override bool RequiresUpdateEveryTick => true;
        public override GoalFlags Flags => GoalFlags.Move | GoalFlags.Look | GoalFlags.Jump;
        public override bool CanUse() => true;

        public override ValueTask TickAsync()
        {
            this.Inner.Run();
            return default;
        }
    }

    // A goal whose code is a plugin's: every call into it is a call into the plugin, refused once the plugin started
    // unloading, so none of it runs while the plugin is released; its mobs then drop it (Mob.ReleasePluginObjects).
    private sealed class PluginGoal(Goal inner, PluginContainer owner) : Goal
    {
        public Goal Inner { get; } = inner;
        public override GoalFlags Flags => this.Inner.Flags;
        public override bool IsInterruptible => this.Inner.IsInterruptible;
        public override bool RequiresUpdateEveryTick => this.Inner.RequiresUpdateEveryTick;

        // Plugins give goals through Obsidian.API, as IGoals; the server's own Goal classes aren't plugin API, so goals
        // built on them aren't wrapped (the unload sweep still removes those whose type is a plugin's).
        public static Goal Wrap(Goal goal)
        {
            if (goal is not LegacyGoal legacy)
                return goal;

            var context = AssemblyLoadContext.GetLoadContext(legacy.Inner.GetType().Assembly);

            return context is PluginLoadContext { Container: PluginContainer owner }
                ? new PluginGoal(goal, owner)
                : goal;
        }

        public override bool CanUse()
        {
            using var call = owner.TryEnter();
            return call is not null && this.Inner.CanUse();
        }

        public override bool CanContinue()
        {
            using var call = owner.TryEnter();
            return call is not null && this.Inner.CanContinue();
        }

        public override void Start()
        {
            using var call = owner.TryEnter();
            if (call is not null)
                this.Inner.Start();
        }

        public override void Stop()
        {
            using var call = owner.TryEnter();
            if (call is not null)
                this.Inner.Stop();
        }

        public override async ValueTask TickAsync()
        {
            using var call = owner.TryEnter();
            if (call is not null)
                await this.Inner.TickAsync();
        }
    }
}
