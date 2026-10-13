using System.Text.Json.Nodes;

namespace Obsidian.Porting;

/// <summary>
/// A NeoForge holder set reduced to what Obsidian's sets can say: its entries (identifiers, <c>#tag</c>s, or objects
/// defined inline) less excluded entries, or every entry less excluded ones.
/// </summary>
/// <param name="All">Whether the set starts from every entry rather than <paramref name="Entries"/>.</param>
/// <param name="Entries">The entries, when not <paramref name="All"/>.</param>
/// <param name="Excluded">The entries left out.</param>
internal sealed record HolderSet(bool All, List<JsonNode> Entries, List<JsonNode> Excluded)
{
    /// <summary>
    /// Reads a holder set: an identifier or <c>#tag</c>, a list, an object defined inline, or NeoForge's
    /// <c>neoforge:any</c>, <c>and</c>, <c>or</c> and <c>not</c> sets.
    /// </summary>
    /// <exception cref="UnsupportedException">
    /// The set is another kind, or combines sets in a way Obsidian's sets can't say, like an intersection.
    /// </exception>
    public static HolderSet Read(JsonNode? value)
    {
        switch (value)
        {
            case JsonValue single when single.TryGetValue<string>(out _):
                return new HolderSet(false, [single], []);

            case JsonArray list when list.All(entry => entry is not null):
                return new HolderSet(false, [.. list.Select(entry => entry!)], []);

            case JsonObject set when set["type"] is JsonValue type
                && type.TryGetValue<string>(out var name)
                && name.StartsWith("neoforge:", StringComparison.Ordinal):
                return name switch
                {
                    "neoforge:any" => new HolderSet(true, [], []),
                    "neoforge:or" => Union(Values(set)),
                    "neoforge:and" => Intersection(Values(set)),
                    "neoforge:not" => Complement(Read(set["value"])),
                    _ => throw new UnsupportedException($"the holder set type {name} has no Obsidian equivalent")
                };

            // An entry defined inline, like a placed feature.
            case JsonObject inline:
                return new HolderSet(false, [inline], []);

            default:
                throw new UnsupportedException("a holder set must be an identifier, a #tag, a list or an object");
        }
    }

    /// <summary>
    /// The entries of a set that has to be listed, like the features a modifier adds.
    /// </summary>
    /// <param name="what">What the set holds, for the error.</param>
    /// <exception cref="UnsupportedException">The set is every entry, or excludes some.</exception>
    public List<JsonNode> Listed(string what)
    {
        if (this.All || this.Excluded.Count > 0)
        {
            throw new UnsupportedException(
                $"its {what} must be listed: Obsidian can't take every one or exclude some");
        }

        return this.Entries;
    }

    private static List<HolderSet> Values(JsonObject set) =>
        set["values"] is JsonArray values
            ? [.. values.Select(Read)]
            : throw new UnsupportedException("a neoforge:and or neoforge:or set has no values list");

    private static HolderSet Union(List<HolderSet> sets)
    {
        if (sets.Any(set => set.All))
            return new HolderSet(true, [], []);

        if (sets.Any(set => set.Excluded.Count > 0))
            throw new UnsupportedException("a neoforge:or of sets that exclude entries has no Obsidian equivalent");

        return new HolderSet(false, [.. sets.SelectMany(set => set.Entries)], []);
    }

    // An intersection of at most one listed set with complements: the listed set less what the others exclude.
    private static HolderSet Intersection(List<HolderSet> sets)
    {
        var listed = sets.Where(set => !set.All).ToList();

        if (listed.Count > 1)
        {
            throw new UnsupportedException(
                "a neoforge:and of two listed sets is an intersection, which Obsidian's sets can't express");
        }

        var excluded = sets.SelectMany(set => set.Excluded).ToList();

        return listed.Count == 0
            ? new HolderSet(true, [], excluded)
            : new HolderSet(false, listed[0].Entries, excluded);
    }

    private static HolderSet Complement(HolderSet set)
    {
        if (set.All)
            throw new UnsupportedException("a neoforge:not of every entry is empty");

        if (set.Excluded.Count > 0)
            throw new UnsupportedException("a neoforge:not of a set that excludes entries has no Obsidian equivalent");

        return new HolderSet(true, [], set.Entries);
    }
}

/// <summary>
/// Thrown when part of a mod has no equivalent in Obsidian's formats; the message says why, to report.
/// </summary>
/// <param name="message">Why, starting in lower case.</param>
internal sealed class UnsupportedException(string message) : Exception(message);
