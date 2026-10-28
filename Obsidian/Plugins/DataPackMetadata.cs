using Microsoft.Extensions.Logging;
using Obsidian.Registries.Codecs;
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
    /// <remarks>
    /// Vanilla reads the <c>pack</c> section with its formats, and failing that with only its description: a pack
    /// without a valid description isn't loaded at all, so its filter doesn't apply either, while a pack whose formats
    /// aren't valid is logged and still loaded (vanilla's server enables packs whatever their formats), filter and
    /// overlays included.
    /// </remarks>
    /// <param name="pack">The pack's name, for messages.</param>
    /// <exception cref="InvalidDataException">
    /// The file isn't JSON, or has no <c>pack</c> object with a valid description.
    /// </exception>
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

            try
            {
                var description = CodecJson.Required(section, "pack", "description");
                TextComponentJson.Check(description, "pack.description");
            }
            catch (InvalidDataException ex)
            {
                throw new InvalidDataException($"{pack}'s {Name} has no valid description: {ex.Message}", ex);
            }

            try
            {
                var formats = ReadFormats(section, "supported_formats", isPack: true);
                formats.Range("Pack", isPack: true, oldFormats: false);
            }
            catch (InvalidDataException ex)
            {
                Log.FormatsNotValid(logger, pack, ex.Message);
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
    /// <c>namespace</c> or <c>path</c> matches every one. Patterns are Java's (see <see cref="JavaRegex"/>) and match
    /// anywhere in the text, like Java's <c>Pattern.asPredicate</c>. This decides which of a pack's files vanilla
    /// applies, so it must match.
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
        var lowest = read.Select(overlay => overlay.Formats.EffectiveMinMajor).DefaultIfEmpty(int.MaxValue).Min();
        var applied = ImmutableArray.CreateBuilder<string>();

        foreach (var (directory, formats) in read)
        {
            // Vanilla warns about an entry with no formats at all, and skips it.
            if (formats.Min is null && formats.Max is null && formats.Supported is null)
                continue;

            var name = $"Overlay \"{directory}\"";
            var (min, max) = formats.Range(name, isPack: false, oldFormats: lowest <= LastPreMinorFormat);
            if (Compare(min, ServerConstants.DataPackFormat) <= 0 && Compare(ServerConstants.DataPackFormat, max) <= 0)
                applied.Add(directory);
        }

        return applied.ToImmutable();
    }

    private static (string Directory, FormatFields Formats) ReadOverlay(JsonElement entry)
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

        return (name, ReadFormats(entry, "formats", isPack: false));
    }

    // A pack's or an overlay's format fields as written: min_format and max_format, the range of old formats
    // (supportedField) and, for a pack, pack_format.
    private static FormatFields ReadFormats(JsonElement section, string supportedField, bool isPack)
    {
        var min = CodecJson.TryGet(section, "min_format", out var minFormat)
            ? Format(minFormat, 0)
            : default((int, int)?);

        var max = CodecJson.TryGet(section, "max_format", out var maxFormat)
            ? Format(maxFormat, int.MaxValue)
            : default((int, int)?);

        var supported = CodecJson.TryGet(section, supportedField, out var range)
            ? Formats(range, supportedField)
            : default((int, int)?);

        var packFormat = isPack && CodecJson.TryGet(section, "pack_format", out var format)
            ? Int(format)
            : default(int?);

        return new FormatFields(min, max, packFormat, supported);
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
    private static (int Min, int Max) Formats(JsonElement value, string path)
    {
        (int Min, int Max) range = value.ValueKind switch
        {
            JsonValueKind.Number => (Int(value), Int(value)),
            JsonValueKind.Array when value.GetArrayLength() == 2 => (Int(value[0]), Int(value[1])),
            JsonValueKind.Object => (
                Int(Field(value, "min_inclusive", path)),
                Int(Field(value, "max_inclusive", path))),
            _ => throw new InvalidDataException($"{path}: {value} isn't a range")
        };

        if (range.Min > range.Max)
            throw new InvalidDataException($"{path}: {range.Min} is greater than {range.Max}");

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
            return JavaRegex.Create(value.GetString()!);
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

    /// <summary>
    /// A pack's or an overlay's format fields as written, which <see cref="Range"/> checks like vanilla's
    /// <c>PackFormat.IntermediaryFormat.validate</c>.
    /// </summary>
    /// <param name="Supported">The range of old formats: a pack's <c>supported_formats</c>, an overlay's
    /// <c>formats</c>.</param>
    /// <param name="PackFormat">A pack's <c>pack_format</c>; overlays have none.</param>
    private sealed record FormatFields(
        (int Major, int Minor)? Min,
        (int Major, int Minor)? Max,
        int? PackFormat,
        (int Min, int Max)? Supported)
    {
        public int EffectiveMinMajor => this.Min is { } min
            ? Math.Min(min.Major, this.Supported?.Min ?? int.MaxValue)
            : this.Supported?.Min ?? int.MaxValue;

        /// <summary>
        /// The formats a pack or overlay is for. Vanilla's rules for them decide which overlays apply, so they must
        /// match: older game versions read <c>pack_format</c> and the range of old formats, newer ones
        /// <c>min_format</c> and <c>max_format</c>, which must agree.
        /// </summary>
        /// <param name="name">What declares the formats, for messages: <c>Pack</c> or <c>Overlay "name"</c>.</param>
        /// <param name="isPack">Whether it's a pack's section, which also needs <c>pack_format</c> for old formats.
        /// </param>
        /// <param name="oldFormats">Whether any overlay of the pack is for an old format, which makes the range of
        /// old formats required.</param>
        public ((int, int) Min, (int, int) Max) Range(string name, bool isPack, bool oldFormats)
        {
            var supportedName = isPack ? "supported_formats" : "formats";

            if (this.Min.HasValue != this.Max.HasValue)
                throw new InvalidDataException($"{name} must declare both min_format and max_format");

            if (oldFormats && this.Supported is null)
            {
                throw new InvalidDataException(
                    $"{name} needs {supportedName}, as an overlay of the pack is for format {LastPreMinorFormat} or "
                    + "older");
            }

            if (this.Min is { } min && this.Max is { } max)
            {
                if (Compare(min, max) > 0)
                    throw new InvalidDataException($"{name}'s min_format is greater than its max_format");

                if (min.Major > LastPreMinorFormat && !oldFormats)
                {
                    if (this.Supported is not null)
                        throw new InvalidDataException($"{name}: {supportedName} is deprecated, remove it");

                    if (isPack && this.PackFormat is int format)
                        CheckPackFormat(name, format, min.Major, max.Major);
                }
                else if (this.Supported is not { } supported)
                {
                    throw new InvalidDataException(
                        $"{name} needs {supportedName} for formats {LastPreMinorFormat} or older");
                }
                else if (supported.Min != min.Major
                    || supported.Max != max.Major && supported.Max != LastPreMinorFormat)
                {
                    throw new InvalidDataException(
                        $"{name}'s {supportedName} don't match its min_format and max_format");
                }
                else if (isPack)
                {
                    CheckPackFormat(name, this.PackFormat, min.Major, max.Major);
                }

                return (min, max);
            }

            if (this.Supported is { } old)
            {
                if (old.Max > LastPreMinorFormat)
                {
                    throw new InvalidDataException(
                        $"{name} needs min_format and max_format for formats after {LastPreMinorFormat}");
                }

                if (isPack)
                    CheckPackFormat(name, this.PackFormat, old.Min, old.Max);

                // Old formats have no minor version: 82.0 and later are newer than any of them.
                return ((old.Min, 0), (old.Max, 0));
            }

            if (isPack && this.PackFormat is int only)
            {
                if (only > LastPreMinorFormat)
                {
                    throw new InvalidDataException(
                        $"{name} needs min_format and max_format for formats after {LastPreMinorFormat}");
                }

                return ((only, 0), (only, 0));
            }

            throw new InvalidDataException($"{name} declares no formats");
        }

        // A pack's pack_format, which older game versions read, must be in its range of formats, from 15 on.
        private static void CheckPackFormat(string name, int? format, int min, int max)
        {
            if (format is not int declared)
                throw new InvalidDataException($"{name} needs pack_format for formats {LastPreMinorFormat} or older");

            if (declared < min || declared > max)
                throw new InvalidDataException($"{name}'s pack_format {declared} isn't from {min} to {max}");

            if (declared < 15)
                throw new InvalidDataException($"{name}'s pack_format {declared} is below 15, the lowest one allowed");
        }
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Data pack {Name}'s {Section} section isn't applied: {Message}")]
        public static partial void SectionLeftOut(ILogger logger, string name, string section, string message);

        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Data pack {Name}'s formats aren't valid for vanilla 1.21.11, which still loads it: {Message}")]
        public static partial void FormatsNotValid(ILogger logger, string name, string message);
    }
}
