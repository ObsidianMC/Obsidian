using Microsoft.Extensions.Logging;
using Obsidian.API.Loot;
using System.Collections.Frozen;
using System.IO;
using System.Text;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>Reloadable table overrides, rebuilt in plugin then data pack load order.</summary>
internal static class PluginLootTables
{
    private static FrozenDictionary<string, LootTable> tables = FrozenDictionary<string, LootTable>.Empty;

    public static LootTable? Resolve(string identifier) =>
        Volatile.Read(ref tables).GetValueOrDefault(identifier) ?? LootTables.All.GetValueOrDefault(identifier);

    /// <summary>Keeps root and nested table lookups on one reload version for a complete evaluation.</summary>
    internal static Func<string, LootTable?> CaptureResolver()
    {
        var snapshot = Volatile.Read(ref tables);
        return identifier => snapshot.GetValueOrDefault(identifier) ?? LootTables.All.GetValueOrDefault(identifier);
    }

    public static void Apply(IEnumerable<LootTable> loaded)
    {
        var result = new Dictionary<string, LootTable>(StringComparer.Ordinal);
        foreach (var table in loaded)
            result[table.Identifier] = table;

        Volatile.Write(ref tables, result.ToFrozenDictionary(StringComparer.Ordinal));
    }

    public static ImmutableArray<LootTable> Read(Plugins.DataFiles source, ILogger logger)
    {
        var result = ImmutableArray.CreateBuilder<LootTable>();
        foreach (var (path, identifier) in FindFiles(source.Paths))
        {
            try
            {
                var bytes = source.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                result.Add(LootTableJson.Parse(Encoding.UTF8.GetString(bytes), identifier));
            }
            catch (InvalidDataException ex)
            {
                logger.LogWarning(ex, "Could not read loot table {Path} from {Source}", path, source.Location);
            }
        }

        return result.ToImmutable();
    }

    public static IEnumerable<(string Path, string Identifier)> FindFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var parts = path.Split('/', 4);
            if (parts.Length != 4 || parts[0] != "data" || parts[2] != "loot_table"
                || !parts[3].EndsWith(".json", StringComparison.Ordinal))
                continue;

            var identifier = parts[1] + ":" + parts[3][..^5];
            if (PluginTags.IsFileIdentifier(identifier))
                yield return (path, identifier);
        }
    }
}
