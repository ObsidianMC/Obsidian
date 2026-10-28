using Obsidian.API;
using Obsidian.API.AI;

namespace Obsidian.Entities.AI;

/// <summary>
/// The path search for any body: a mob, or a plugin's own <see cref="IPathBody"/>. Call on the server tick thread.
/// </summary>
public sealed class PathSearch : IPathfinder
{
    private readonly PathFinder finder;

    private PathSearch(IPathBody body) => finder = new PathFinder(body);

    public static IPathfinder For(IPathBody body)
    {
        ArgumentNullException.ThrowIfNull(body);
        return new PathSearch(body);
    }

    public NavigationPath? FindPath(VectorD target)
    {
        if (!double.IsFinite(target.X) || !double.IsFinite(target.Y) || !double.IsFinite(target.Z))
            throw new ArgumentOutOfRangeException(nameof(target));

        return finder.FindPath(target) is { } found ? new NavigationPath(found.Nodes, found.ReachedTarget) : null;
    }
}
