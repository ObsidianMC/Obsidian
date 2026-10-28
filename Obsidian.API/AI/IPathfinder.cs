namespace Obsidian.API.AI;

/// <summary>
/// Computes the path a mob would walk, using the same search as its navigator.
/// Implemented by the server's navigator; cast <see cref="IEntity.Navigator"/> to use it.
/// Call on the server tick thread, like the rest of a mob's AI.
/// </summary>
public interface IPathfinder
{
    /// <summary>Finds a path to <paramref name="target"/>. Returns null when no path exists within range.</summary>
    NavigationPath? FindPath(VectorD target);
}

/// <summary>A computed path: the nodes in walking order, and whether the last node is the target's block.</summary>
public sealed record NavigationPath(IReadOnlyList<VectorD> Nodes, bool ReachedTarget);
