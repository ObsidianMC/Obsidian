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
    /// The candidates that can load, in an order where each loads after the dependencies it needs, and those that can't,
    /// with the reason.
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

                var reason = Check(candidate, present, rejected, side, warnings);
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

    private static string? Check(PluginInfo candidate, Dictionary<string, PluginInfo> present,
        Dictionary<PluginInfo, string> rejected, PluginSide side, List<string> warnings)
    {
        foreach (var dependency in candidate.Dependencies)
        {
            if (!AppliesTo(dependency, side))
                continue;

            if (!VersionRange.TryParse(dependency.Version, out var range))
                return $"its dependency '{dependency.Id}' has an invalid version range '{dependency.Version}'";

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

    // Kahn's algorithm, taking the lowest id among the plugins that are ready so that the order doesn't depend on the
    // order the files were found in.
    private static List<PluginInfo> Sort(List<PluginInfo> accepted, PluginSide side,
        Dictionary<PluginInfo, string> rejected)
    {
        var byId = accepted.ToDictionary(info => info.Id);
        var after = accepted.ToDictionary(info => info, _ => new List<PluginInfo>());
        var waitingOn = accepted.ToDictionary(info => info, _ => 0);

        foreach (var plugin in accepted)
        {
            foreach (var dependency in plugin.Dependencies)
            {
                if (!AppliesTo(dependency, side) || !byId.TryGetValue(dependency.Id, out var other))
                    continue;

                if (dependency.Type is PluginDependencyType.Incompatible or PluginDependencyType.Discouraged)
                    continue;

                // A present dependency loads first, so the plugin can use its types, unless ordered otherwise.
                var (first, second) = dependency.Ordering == PluginDependencyOrdering.Before
                    ? (plugin, other)
                    : (other, plugin);

                after[first].Add(second);
                waitingOn[second]++;
            }
        }

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

    public sealed class Resolution
    {
        public required ImmutableArray<PluginInfo> LoadOrder { get; init; }

        public required ImmutableDictionary<PluginInfo, string> Rejected { get; init; }

        public required ImmutableArray<string> Warnings { get; init; }
    }
}
