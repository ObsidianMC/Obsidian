namespace Obsidian.API.AI;

public interface IGoalController
{
    public bool IsPaused { get; }
    public bool IsExecuting { get; }

    public void AddGoal(IGoal goal);
    public void RemoveGoal(IGoal goal);

    public void Clear();
    public void Cancel();
    public void Pause();
}
