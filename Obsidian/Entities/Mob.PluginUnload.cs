namespace Obsidian.Entities;

public partial class Mob
{
    /// <summary>Detaches a removed mob without calling its plugin's goals, navigator or removal callbacks.</summary>
    internal void DetachForPluginUnload()
    {
        this.EjectPassengers();
        this.LeaveVehicle();
        this.IsRemoved = true;
        this.AttackTarget = null;
        this.AlertedTarget = null;
        this.LastAttacker = null;
        this.goals.Discard();
        this.TargetGoals.Discard();

        if (this.GoalController is AI.GoalSelector selector)
            selector.Discard();

        this.GoalController = null;
        this.Navigator = null;
        this.MoveControl = new AI.MoveControl(this);
    }

    /// <summary>Drops host AI references to evicted entities between ticks, without invoking plugin code.</summary>
    internal void ForgetEvictedEntities(HashSet<IEntity> removed)
    {
        if (this.AttackTarget is IEntity target && removed.Contains(target))
            this.AttackTarget = null;

        if (this.AlertedTarget is IEntity alerted && removed.Contains(alerted))
            this.AlertedTarget = null;

        if (this.LastAttacker is IEntity attacker && removed.Contains(attacker))
            this.LastAttacker = null;

        this.goals.ForgetEvictedEntities(removed);
        this.TargetGoals.ForgetEvictedEntities(removed);

        if (this.GoalController is AI.GoalSelector selector)
            selector.ForgetEvictedEntities(removed);
    }
}
