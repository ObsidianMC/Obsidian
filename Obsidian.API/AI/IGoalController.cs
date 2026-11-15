namespace Obsidian.API.AI;

/// <summary>
/// Manages the goals selected for an entity's AI updates.
/// </summary>
public interface IGoalController
{
    /// <summary>
    /// Gets whether goal selection and execution are paused.
    /// </summary>
    public bool IsPaused { get; }
    /// <summary>
    /// Gets whether any registered goal is running.
    /// </summary>
    public bool IsExecuting { get; }

    /// <summary>
    /// Registers a goal for selection; plain <see cref="IGoal"/> instances are wrapped and are not deduplicated.
    /// </summary>
    public void AddGoal(IGoal goal);
    /// <summary>
    /// Stops and removes the matching goal instance.
    /// </summary>
    public void RemoveGoal(IGoal goal);

    /// <summary>
    /// Stops all running goals and removes every registration.
    /// </summary>
    public void Clear();
    /// <summary>
    /// Stops running goals while retaining their registrations for later selection.
    /// </summary>
    public void Cancel();
    /// <summary>
    /// Stops running goals and pauses further goal updates.
    /// </summary>
    public void Pause();
}
