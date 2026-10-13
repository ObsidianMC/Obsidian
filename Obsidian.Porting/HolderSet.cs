using System.Text.Json.Nodes;

namespace Obsidian.Porting;

/// <summary>
/// A NeoForge holder set reduced to what Obsidian's sets can say: its entries (identifiers, <c>#tag</c>s, or objects
/// defined inline) less excluded entries, or every entry less excluded ones.
/// </summary>
/// <remarks>
/// What tags hold isn't known here, so sets combine only when the result can be listed without knowing it: a tag
/// and another entry may share entries, two different identifiers never do.
/// </remarks>
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
    /// Whether two of its entries may hold the same entry, which NeoForge's sets hold once: a tag listed with
    /// another tag or an identifier.
    /// </summary>
    public bool MayOverlap => !this.All && this.Entries.Count > 1 && this.Entries.Any(IsTag);

    /// <summary>
    /// The entries of a set that has to be listed, like the features a modifier adds.
    /// </summary>
    /// <param name="what">What the set holds, for the error.</param>
    /// <param name="additive">
    /// Whether each entry is added, like features, rather than looked for, like the entity types whose spawns are
    /// removed: then entries that may overlap would add what they share twice.
    /// </param>
    /// <exception cref="UnsupportedException">
    /// The set is every entry, or excludes some, or it's <paramref name="additive"/> and <see cref="MayOverlap"/>.
    /// </exception>
    public List<JsonNode> Listed(string what, bool additive = false)
    {
        if (this.All || this.Excluded.Count > 0)
        {
            throw new UnsupportedException(
                $"its {what} must be listed: Obsidian can't take every one or exclude some");
        }

        if (additive && this.MayOverlap)
        {
            throw new UnsupportedException(
                $"its {what} join tags that may share entries, which Obsidian would add twice and NeoForge once");
        }

        return this.Entries;
    }

    private static List<HolderSet> Values(JsonObject set) =>
        set["values"] is JsonArray values
            ? [.. values.Select(Read)]
            : throw new UnsupportedException("a neoforge:and or neoforge:or set has no values list");

    // A union, each entry once. With sets of every entry less some, it's every entry less what all of those exclude
    // and no listed set holds.
    private static HolderSet Union(List<HolderSet> sets)
    {
        if (sets.Count == 1)
            return sets[0];

        var listed = sets.Where(set => !set.All).ToList();

        if (listed.Any(set => set.Excluded.Count > 0))
            throw new UnsupportedException("a neoforge:or of sets that exclude entries has no Obsidian equivalent");

        var entries = Distinct(listed.SelectMany(set => set.Entries));
        var complements = sets.Where(set => set.All).ToList();

        if (complements.Count == 0)
            return new HolderSet(false, entries, []);

        var excluded = complements[0].Excluded;

        foreach (var complement in complements.Skip(1))
            excluded = Intersect(excluded, complement.Excluded);

        return new HolderSet(true, [], Subtract(excluded, entries));
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

        var excluded = Distinct(sets.SelectMany(set => set.Excluded));

        return listed.Count == 0
            ? new HolderSet(true, [], excluded)
            : new HolderSet(false, listed[0].Entries, excluded);
    }

    // The complement of every entry less some is those; of a listed set less some, every entry less the rest.
    private static HolderSet Complement(HolderSet set)
    {
        if (!set.All)
            return new HolderSet(true, [], Subtract(set.Entries, set.Excluded));

        if (set.Excluded.Count == 0)
            throw new UnsupportedException("a neoforge:not of every entry is empty");

        return new HolderSet(false, set.Excluded, []);
    }

    // The entries both lists hold, when that doesn't depend on what tags hold.
    private static List<JsonNode> Intersect(List<JsonNode> first, List<JsonNode> second)
    {
        var keys = second.Select(Key).ToHashSet(StringComparer.Ordinal);
        var common = first.Where(entry => keys.Contains(Key(entry))).ToList();
        var rest = first.Where(entry => !keys.Contains(Key(entry)));

        if (rest.Any(entry => second.Any(other => MayShare(entry, other))))
        {
            throw new UnsupportedException(
                "a neoforge:or of sets excluding different tags has no Obsidian equivalent");
        }

        return common;
    }

    // The entries of the first list the second doesn't hold, when that doesn't depend on what tags hold.
    private static List<JsonNode> Subtract(List<JsonNode> entries, List<JsonNode> removed)
    {
        var keys = removed.Select(Key).ToHashSet(StringComparer.Ordinal);
        var rest = entries.Where(entry => !keys.Contains(Key(entry))).ToList();

        if (rest.Any(entry => removed.Any(other => MayShare(entry, other))))
        {
            throw new UnsupportedException(
                "a set less entries that may share some with its tags, or the reverse, has no Obsidian equivalent");
        }

        return rest;
    }

    // Whether two different entries may hold the same entry: when either is a tag. Identifiers and objects defined
    // inline are one entry each.
    private static bool MayShare(JsonNode entry, JsonNode other) =>
        Key(entry) != Key(other) && (IsTag(entry) || IsTag(other));

    private static bool IsTag(JsonNode entry) =>
        entry is JsonValue value && value.TryGetValue<string>(out var name) && name.StartsWith('#');

    private static List<JsonNode> Distinct(IEnumerable<JsonNode> entries) => [.. entries.DistinctBy(Key)];

    private static string Key(JsonNode entry) => entry.ToJsonString();
}

/// <summary>
/// Thrown when part of a mod has no equivalent in Obsidian's formats; the message says why, to report.
/// </summary>
/// <param name="message">Why, starting in lower case.</param>
internal sealed class UnsupportedException(string message) : Exception(message);
