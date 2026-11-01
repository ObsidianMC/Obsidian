using Microsoft.Extensions.Logging;

namespace Obsidian.Registries;

/// <summary>
/// What plugins' and data packs' files share when they define the same entries, vanilla's included: the last file in
/// order wins, whole, like vanilla's data packs.
/// </summary>
internal static partial class VanillaOverrides
{
    /// <summary>
    /// Keeps, of the files defining each entry, the last in order, in the order entries were first defined. Each
    /// replaced file is logged.
    /// </summary>
    /// <param name="files">Plugins' files in load order, then data packs' in theirs.</param>
    public static List<T> LastWins<T>(
        IEnumerable<T> files,
        Func<T, (string Registry, string Identifier)> entry,
        Func<T, string> source,
        ILogger logger)
    {
        var byEntry = new OrderedDictionary<(string Registry, string Identifier), T>();

        foreach (var file in files)
        {
            var key = entry(file);
            if (byEntry.TryGetValue(key, out var replaced))
            {
                var winner = source(file);
                var loser = source(replaced);
                Log.Replaced(logger, key.Identifier, winner, loser);
            }

            byEntry[key] = file;
        }

        return [.. byEntry.Values];
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information,
            Message = "{Identifier} from {Source} replaces the one from {Replaced}")]
        public static partial void Replaced(ILogger logger, string identifier, string source, string replaced);
    }
}
