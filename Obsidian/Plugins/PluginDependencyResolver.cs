using NuGet.Versioning;
using Obsidian.API.Plugins;

namespace Obsidian.Plugins;

/// <summary>
/// Decides which plugins can load and in what order, following the dependency rules of NeoForge's mod metadata:
/// required dependencies must be present with an accepted version, optional ones must have an accepted version if
/// present, incompatible ones must be absent, and discouraged ones only warn.
/// </summary>
internal static class PluginDependencyResolver
{
    /// <summary>
    /// Resolves the plugins about to load against each other and against the plugins already loaded.
    /// </summary>
    /// <param name="candidates">The plugins to load.</param>
    /// <param name="loaded">The plugins already loaded, which candidates can depend on.</param>
    /// <param name="side">The side this runs on; dependencies declared for the other side are ignored.</param>
    /// <returns>
    /// The candidates that can load, in an order where each loads after the dependencies it needs, and those that
    /// can't, with the reason.
    /// </returns>
    public static Resolution Resolve(IReadOnlyList<PluginInfo> candidates, IReadOnlyList<PluginInfo> loaded,
        PluginSide side)
    {
        var rejected = new Dictionary<PluginInfo, string>();
        var warnings = new List<string>();

        // Like two mods with one id, a candidate can't share its id with a loaded plugin or an earlier candidate.
        var present = loaded.ToDictionary(info => info.Id);
        foreach (var candidate in candidates)
        {
            if (!present.TryAdd(candidate.Id, candidate))
                rejected[candidate] = $"another plugin with the id '{candidate.Id}' is already loaded";
        }

        // Rejecting a plugin can break the plugins that require it, so this repeats until nothing changes.
        bool changed;
        do
        {
            changed = false;

            foreach (var candidate in candidates)
            {
                if (rejected.ContainsKey(candidate))
                    continue;

                var reason = Check(candidate, present, side, warnings)
                    ?? CheckLoaded(candidate, loaded, side, warnings);
                if (reason is null)
                    continue;

                rejected[candidate] = reason;
                present.Remove(candidate.Id);
                changed = true;
            }
        }
        while (changed);

        var accepted = candidates.Where(candidate => !rejected.ContainsKey(candidate)).ToList();
        var order = Sort(accepted, side, rejected);

        return new Resolution
        {
            LoadOrder = [.. order],
            Rejected = rejected.ToImmutableDictionary(),
            Warnings = [.. warnings.Distinct()]
        };
    }

    /// <summary>
    /// Whether a dependency matters on this side.
    /// </summary>
    public static bool AppliesTo(PluginDependency dependency, PluginSide side) =>
        dependency.Side == PluginSide.Both || dependency.Side == side;

    /// <summary>
    /// Finds a required dependency of <paramref name="info"/> that isn't loaded, like one that failed to load after
    /// <see cref="Resolve"/> accepted it.
    /// </summary>
    /// <returns>The missing dependency's id, or null if every required dependency on this side is loaded.</returns>
    public static string? FindMissingDependency(PluginInfo info, IEnumerable<PluginInfo> loaded, PluginSide side)
    {
        var loadedIds = loaded.Select(plugin => plugin.Id).ToHashSet();

        return info.Dependencies
            .Where(dependency => dependency.Required && AppliesTo(dependency, side))
            .Select(dependency => dependency.Id)
            .FirstOrDefault(id => !loadedIds.Contains(id));
    }

    // The candidate's own dependencies.
    private static string? Check(PluginInfo candidate, Dictionary<string, PluginInfo> present, PluginSide side,
        List<string> warnings)
    {
        foreach (var dependency in candidate.Dependencies)
        {
            if (!AppliesTo(dependency, side))
                continue;

            if (!VersionRange.TryParse(dependency.Version, out var range))
                return $"its dependency '{dependency.Id}' has an invalid version range '{dependency.Version}'";

            // Obsidian loads a plugin's required dependencies first, for their types, so it can't come before them.
            if (dependency.Type == PluginDependencyType.Required
                && dependency.Ordering == PluginDependencyOrdering.Before)
            {
                return $"it requires '{dependency.Id}' but orders itself before it";
            }

            var found = present.GetValueOrDefault(dependency.Id);
            var accepted = found is not null && range.Satisfies(new NuGetVersion(found.Version));

            switch (dependency.Type)
            {
                case PluginDependencyType.Required when found is null:
                    return $"it requires '{dependency.Id}' {dependency.Version}, which isn't loaded";
                case PluginDependencyType.Required or PluginDependencyType.Optional when found is not null && !accepted:
                    return $"it needs '{dependency.Id}' {dependency.Version}, but {found!.Version} is loaded";
                case PluginDependencyType.Incompatible when accepted:
                    return $"it is incompatible with '{dependency.Id}' {found!.Version}";
                case PluginDependencyType.Discouraged when accepted:
                    warnings.Add($"{candidate.Name} discourages using '{dependency.Id}' {found!.Version} with it");
                    break;
            }
        }

        return null;
    }

    // What the loaded plugins declare about the candidate: a plugin arriving later mustn't break their constraints.
    private static string? CheckLoaded(PluginInfo candidate, IReadOnlyList<PluginInfo> loaded, PluginSide side,
        List<string> warnings)
    {
        var version = new NuGetVersion(candidate.Version);

        foreach (var plugin in loaded)
        {
            foreach (var dependency in plugin.Dependencies)
            {
                if (dependency.Id != candidate.Id
                    || !AppliesTo(dependency, side)
                    || !VersionRange.TryParse(dependency.Version, out var range))
                {
                    continue;
                }

                var accepted = range.Satisfies(version);

                switch (dependency.Type)
                {
                    case PluginDependencyType.Required or PluginDependencyType.Optional when !accepted:
                        return $"'{plugin.Id}' needs {dependency.Version} of it";
                    case PluginDependencyType.Incompatible when accepted:
                        return $"'{plugin.Id}' is incompatible with it";
                    case PluginDependencyType.Discouraged when accepted:
                        warnings.Add($"{plugin.Name} discourages using '{candidate.Id}' {candidate.Version} with it");
                        break;
                }
            }
        }

        return null;
    }

    // Kahn's algorithm, taking the lowest id among the plugins that are ready so that the order doesn't depend on the
    // order the files were found in.
    private static List<PluginInfo> Sort(List<PluginInfo> accepted, PluginSide side,
        Dictionary<PluginInfo, string> rejected)
    {
        var byId = accepted.ToDictionary(info => info.Id);
        var after = accepted.ToDictionary(info => info, _ => new List<PluginInfo>());
        var softEdges = new List<(PluginInfo First, PluginInfo Second)>();

        foreach (var plugin in accepted)
        {
            foreach (var dependency in plugin.Dependencies)
            {
                if (!AppliesTo(dependency, side) || !byId.TryGetValue(dependency.Id, out var other))
                    continue;

                if (dependency.Type is PluginDependencyType.Incompatible or PluginDependencyType.Discouraged)
                    continue;

                if (dependency.Ordering == PluginDependencyOrdering.Before)
                    after[plugin].Add(other);
                else if (dependency.Ordering == PluginDependencyOrdering.After || dependency.Required)
                    after[other].Add(plugin);
                else
                    softEdges.Add((other, plugin));
            }
        }

        // An optional dependency without an ordering loads first too, so that the plugin can use its types, unless
        // that contradicts the other orderings (as for two plugins that optionally depend on each other).
        foreach (var (first, second) in softEdges.OrderBy(edge => edge.Second.Id, StringComparer.Ordinal))
        {
            if (!Reaches(after, second, first))
                after[first].Add(second);
        }

        var waitingOn = accepted.ToDictionary(info => info, _ => 0);
        foreach (var second in after.Values.SelectMany(seconds => seconds))
            waitingOn[second]++;

        var ready = new SortedSet<PluginInfo>(
            waitingOn.Where(pair => pair.Value == 0).Select(pair => pair.Key),
            Comparer<PluginInfo>.Create((left, right) => string.CompareOrdinal(left.Id, right.Id)));
        var order = new List<PluginInfo>(accepted.Count);

        while (ready.Count > 0)
        {
            var next = ready.Min!;
            ready.Remove(next);
            order.Add(next);

            foreach (var dependent in after[next])
            {
                if (--waitingOn[dependent] == 0)
                    ready.Add(dependent);
            }
        }

        foreach (var plugin in accepted.Except(order))
            rejected[plugin] = "its dependencies form a cycle";

        return order;
    }

    private static bool Reaches(Dictionary<PluginInfo, List<PluginInfo>> after, PluginInfo from, PluginInfo to)
    {
        var visited = new HashSet<PluginInfo>();
        var pending = new Stack<PluginInfo>([from]);

        while (pending.TryPop(out var plugin))
        {
            if (plugin == to)
                return true;

            if (!visited.Add(plugin))
                continue;

            foreach (var next in after[plugin])
                pending.Push(next);
        }

        return false;
    }

    public sealed class Resolution
    {
        public required ImmutableArray<PluginInfo> LoadOrder { get; init; }

        public required ImmutableDictionary<PluginInfo, string> Rejected { get; init; }

        public required ImmutableArray<string> Warnings { get; init; }
    }
}
