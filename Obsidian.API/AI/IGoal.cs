namespace Obsidian.API.AI;

/// <summary>
/// Provides the synchronous update entry point for an entity AI goal.
/// </summary>
public interface IGoal
{
    /// <summary>
    /// Runs one update of this goal while its controller selects it for execution.
    /// </summary>
    public void Run();
}
