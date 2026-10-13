using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Obsidian.Plugins;

/// <summary>
/// What Obsidian applies of a data pack's <c>pack.mcmeta</c>: its <c>filter</c>, which blocks files of the packs
/// before it, and the <c>overlays</c> applied over its root for vanilla 1.21.11's data pack format, read like vanilla's
/// codecs read them.
/// </summary>
/// <param name="Filter">The <c>filter.block</c> patterns; empty without a filter.</param>
/// <param name="Overlays">The overlay directories applied, in the order they're listed: each one's
/// <c>data/</c> files replace the root's and earlier overlays' of the same path.</param>
internal sealed partial record DataPackMetadata(
    ImmutableArray<DataPackMetadata.Pattern> Filter,
    ImmutableArray<string> Overlays)
{
    /// <summary>
    /// The data pack format vanilla 1.21.11 runs with, 94.1, which picks a pack's overlays. It must match vanilla's, so
    /// that a pack made for several versions applies the same overlays as in vanilla.
    /// </summary>
    public static (int Major, int Minor) CurrentFormat { get; } = (94, 1);

    // Vanilla's last data pack format without a minor version: overlays for it or older ones need a "formats" range.
    private const int LastPreMinorFormat = 81;

    private const string Name = "pack.mcmeta";

    // Gson reads pack.mcmeta leniently, so comments and trailing commas are fine.
    private static readonly JsonDocumentOptions documentOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    /// <summary>
    /// Reads a pack's metadata. Like vanilla, which logs a metadata section it can't read and goes on without it, a
    /// filter or overlays section that isn't valid is logged and not applied.
    /// </summary>
    /// <param name="pack">The pack's name, for messages.</param>
    /// <exception cref="InvalidDataException">The file isn't JSON, or has no <c>pack</c> object.</exception>
    public static DataPackMetadata Read(string pack, byte[] json, ILogger logger)
    {
        try
        {
            using var document = JsonDocument.Parse(json, documentOptions);
            var root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("pack", out var section)
                || section.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"{pack}'s {Name} has no pack object.");
            }

            return new DataPackMetadata(
                ReadSection(pack, root, "filter", ReadFilter, logger),
                ReadSection(pack, root, "overlays", ReadOverlays, logger));
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{pack}'s {Name} isn't valid JSON: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Whether the filter blocks a file of the packs before this one, by its path in a pack
    /// (<c>data/&lt;namespace&gt;/&lt;path&gt;</c>).
    /// </summary>
    /// <remarks>
    /// Like vanilla's, a file is blocked when any pattern matches its namespace and any pattern, not necessarily the
    /// same one, matches its path within the namespace (like <c>worldgen/biome/plains.json</c>); a pattern without a
    /// <c>namespace</c> or <c>path</c> matches every one. Patterns match anywhere in the text, like Java's
    /// <c>Pattern.asPredicate</c>. This decides which of a pack's files vanilla applies, so it must match.
    /// </remarks>
    public bool Blocks(string path)
    {
        if (this.Filter.IsEmpty)
            return false;

        var parts = path.Split('/', 3);
        if (parts.Length < 3 || parts[0] != "data")
            return false;

        return this.Filter.Any(pattern => pattern.Namespace?.IsMatch(parts[1]) ?? true)
            && this.Filter.Any(pattern => pattern.Path?.IsMatch(parts[2]) ?? true);
    }

    private static ImmutableArray<T> ReadSection<T>(
        string pack,
        JsonElement root,
        string name,
        Func<JsonElement, ImmutableArray<T>> read,
        ILogger logger)
    {
        if (!root.TryGetProperty(name, out var section))
            return [];

        try
        {
            return read(section);
        }
        catch (InvalidDataException ex)
        {
            Log.SectionLeftOut(logger, pack, name, ex.Message);
            return [];
        }
    }

    // {"block": [{"namespace": regex, "path": regex}, ...]}
    private static ImmutableArray<Pattern> ReadFilter(JsonElement section)
    {
        var block = Field(Object(section, "filter"), "block", "filter");
        if (block.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("filter.block isn't a list");

        var patterns = ImmutableArray.CreateBuilder<Pattern>();

        foreach (var entry in block.EnumerateArray())
        {
            Object(entry, "filter.block");
            patterns.Add(new Pattern(ReadPattern(entry, "namespace"), ReadPattern(entry, "path")));
        }

        return patterns.ToImmutable();
    }

    // {"entries": [{"directory": name, "min_format": ..., "max_format": ..., "formats": ...}, ...]}: the entries
    // applying to the current format, checked like vanilla's codec checks them.
    private static ImmutableArray<string> ReadOverlays(JsonElement section)
    {
        var entries = Field(Object(section, "overlays"), "entries", "overlays");
        if (entries.ValueKind != JsonValueKind.Array)
            throw new InvalidDataException("overlays.entries isn't a list");

        var read = entries.EnumerateArray().Select(ReadOverlay).ToList();

        // When any overlay is for an old format, all of them need "formats", which old versions read.
        var lowest = read.Select(overlay => overlay.EffectiveMinMajor).DefaultIfEmpty(int.MaxValue).Min();
        var applied = ImmutableArray.CreateBuilder<string>();

        foreach (var overlay in read)
        {
            // Vanilla warns about an entry with no formats at all, and skips it.
            if (overlay.Min is null && overlay.Formats is null)
                continue;

            var (min, max) = overlay.Range(lowest <= LastPreMinorFormat);
            if (Compare(min, CurrentFormat) <= 0 && Compare(CurrentFormat, max) <= 0)
                applied.Add(overlay.Directory);
        }

        return applied.ToImmutable();
    }

    private static Overlay ReadOverlay(JsonElement entry)
    {
        const string path = "overlays.entries";

        Object(entry, path);

        var directory = Field(entry, "directory", path);
        if (directory.ValueKind != JsonValueKind.String
            || directory.GetString() is not string name
            || !DirectoryName().IsMatch(name))
        {
            throw new InvalidDataException($"{path}: {directory} isn't an accepted directory name");
        }

        // Vanilla's name check lets these through; they'd leave the pack.
        if (name is "." or "..")
            throw new InvalidDataException($"{path}: {name} isn't an accepted directory name");

        var min = entry.TryGetProperty("min_format", out var minFormat) ? Format(minFormat, 0) : default((int, int)?);
        var max = entry.TryGetProperty("max_format", out var maxFormat)
            ? Format(maxFormat, int.MaxValue)
            : default((int, int)?);

        var formats = entry.TryGetProperty("formats", out var range) ? Formats(range) : default((int, int)?);

        if (min.HasValue != max.HasValue)
            throw new InvalidDataException($"Overlay \"{name}\" must declare both min_format and max_format");

        return new Overlay(name, min, max, formats);
    }

    // A format: a major version, [major] or [major, minor]; a missing minor is the lowest or highest.
    private static (int Major, int Minor) Format(JsonElement value, int minor)
    {
        if (value.ValueKind == JsonValueKind.Number)
            return (NonNegative(value), minor);

        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() is < 1 or > 256)
            throw new InvalidDataException($"{value} isn't a pack format");

        var major = NonNegative(value[0]);

        return value.GetArrayLength() > 1 ? (major, NonNegative(value[1])) : (major, minor);
    }

    // vanilla's InclusiveRange of ints: n, [min, max] or {"min_inclusive": min, "max_inclusive": max}.
    private static (int Min, int Max) Formats(JsonElement value)
    {
        (int Min, int Max) range = value.ValueKind switch
        {
            JsonValueKind.Number => (Int(value), Int(value)),
            JsonValueKind.Array when value.GetArrayLength() == 2 => (Int(value[0]), Int(value[1])),
            JsonValueKind.Object => (
                Int(Field(value, "min_inclusive", "formats")),
                Int(Field(value, "max_inclusive", "formats"))),
            _ => throw new InvalidDataException($"formats: {value} isn't a range")
        };

        if (range.Min > range.Max)
            throw new InvalidDataException($"formats: {range.Min} is greater than {range.Max}");

        return range;
    }

    private static Regex? ReadPattern(JsonElement entry, string name)
    {
        if (!entry.TryGetProperty(name, out var value))
            return null;

        if (value.ValueKind != JsonValueKind.String)
            throw new InvalidDataException($"filter.block: {name} isn't a string");

        try
        {
            return new Regex(value.GetString()!, RegexOptions.CultureInvariant);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidDataException($"filter.block: {name} isn't a valid pattern: {ex.Message}", ex);
        }
    }

    private static JsonElement Object(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Object
            ? value
            : throw new InvalidDataException($"{path}: {value} isn't an object");

    private static JsonElement Field(JsonElement value, string name, string path) =>
        value.TryGetProperty(name, out var field)
            ? field
            : throw new InvalidDataException($"{path}: missing {name}");

    private static int Int(JsonElement value) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : throw new InvalidDataException($"{value} isn't an integer");

    private static int NonNegative(JsonElement value) =>
        Int(value) is var number and >= 0
            ? number
            : throw new InvalidDataException($"{value} isn't a non-negative integer");

    private static int Compare((int Major, int Minor) a, (int Major, int Minor) b) =>
        a.Major != b.Major ? a.Major.CompareTo(b.Major) : a.Minor.CompareTo(b.Minor);

    [GeneratedRegex("^[-_a-zA-Z0-9.]+$")]
    private static partial Regex DirectoryName();

    /// <summary>
    /// A <c>filter.block</c> pattern; a missing part matches everything.
    /// </summary>
    public sealed record Pattern(Regex? Namespace, Regex? Path);

    // An overlay entry as written; Range checks it like vanilla's PackFormat.IntermediaryFormat.validate.
    private sealed record Overlay(
        string Directory,
        (int Major, int Minor)? Min,
        (int Major, int Minor)? Max,
        (int Min, int Max)? Formats)
    {
        public int EffectiveMinMajor => this.Min is { } min
            ? Math.Min(min.Major, this.Formats?.Min ?? int.MaxValue)
            : this.Formats?.Min ?? int.MaxValue;

        // The formats it applies to; oldFormats is whether any overlay of the pack is for an old format.
        public ((int, int) Min, (int, int) Max) Range(bool oldFormats)
        {
            var name = $"Overlay \"{this.Directory}\"";

            if (oldFormats && this.Formats is null)
            {
                throw new InvalidDataException(
                    $"{name} needs formats, as an overlay of the pack is for format {LastPreMinorFormat} or older");
            }

            if (this.Min is { } min && this.Max is { } max)
            {
                if (Compare(min, max) > 0)
                    throw new InvalidDataException($"{name}'s min_format is greater than its max_format");

                if (min.Major > LastPreMinorFormat && !oldFormats)
                {
                    if (this.Formats is not null)
                        throw new InvalidDataException($"{name}: formats is deprecated, remove it");
                }
                else if (this.Formats is not { } formats)
                {
                    throw new InvalidDataException($"{name} needs formats for formats {LastPreMinorFormat} or older");
                }
                else if (formats.Min != min.Major || formats.Max != max.Major && formats.Max != LastPreMinorFormat)
                {
                    throw new InvalidDataException($"{name}'s formats don't match its min_format and max_format");
                }

                return (min, max);
            }

            var old = this.Formats!.Value;
            if (old.Max > LastPreMinorFormat)
                throw new InvalidDataException($"{name} needs min_format and max_format for formats after 81");

            // Old formats have no minor version: 82.0 and later are newer than any of them.
            return ((old.Min, 0), (old.Max, 0));
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Data pack {Name}'s {Section} section isn't applied: {Message}")]
        public static partial void SectionLeftOut(ILogger logger, string name, string section, string message);
    }
}
