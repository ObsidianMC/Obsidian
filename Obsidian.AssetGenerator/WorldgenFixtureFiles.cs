using System.IO.Enumeration;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.AssetGenerator;

/// <summary>
/// A file of the 26.3 worldgen fixture (<c>Obsidian.Tests/Assets/vanilla_worldgen_26.3</c>), which holds
/// <c>{ "version": ..., "cases": [...] }</c>. A section is a file (<c>noises.json</c>) or a folder of files
/// (<c>features/02-ores.json</c>); the section's cases are its files' cases, in ordinal order of the file names.
/// </summary>
/// <param name="Section">The section, e.g. <c>features</c>.</param>
/// <param name="Name">The file's path in the fixture without <c>.json</c>, e.g. <c>features/02-ores</c>.</param>
/// <param name="Path">The file's full path.</param>
/// <param name="Root">The file's contents.</param>
internal sealed record WorldgenFixtureFile(string Section, string Name, string Path, JsonObject Root)
{
    // Like Gson's pretty printing, which the probe uses: two spaces, LF, and no escaping beyond JSON's own. The values
    // keep their original text (numbers as the probe spelled them), so rewriting a file changes only what was captured.
    private static readonly JsonWriterOptions WriterOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    public JsonArray Cases => this.Root["cases"]!.AsArray();

    /// <summary>The fixture's files, by section and then ordinal file name.</summary>
    public static List<WorldgenFixtureFile> Load(string directory)
    {
        var files = new List<WorldgenFixtureFile>();
        foreach (var path in Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories))
        {
            var relative = System.IO.Path.GetRelativePath(directory, path).Replace('\\', '/');
            var name = relative[..^".json".Length];
            var section = name.Split('/')[0];
            var root = JsonNode.Parse(File.ReadAllText(path))!.AsObject();

            files.Add(new WorldgenFixtureFile(section, name, path, root));
        }

        return [.. files.OrderBy(file => file.Name, StringComparer.Ordinal)];
    }

    /// <summary>Writes the file when its contents changed, deterministically.</summary>
    /// <returns>Whether it changed.</returns>
    public bool Save()
    {
        var contents = Serialize(this.Root);
        if (File.Exists(this.Path) && File.ReadAllText(this.Path).ReplaceLineEndings("\n") == contents)
            return false;

        var writing = this.Path + ".tmp";
        File.WriteAllText(writing, contents);
        File.Move(writing, this.Path, overwrite: true);
        return true;
    }

    /// <summary>The fixture's format: indented with two spaces, LF line endings and a trailing newline.</summary>
    public static string Serialize(JsonNode node)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, WriterOptions))
            node.WriteTo(writer);

        return Encoding.UTF8.GetString(buffer.ToArray()) + "\n";
    }
}

/// <summary>
/// The fixture's cases a capture records: those of the selected files whose names match, or all of them.
/// </summary>
/// <param name="Sections">
/// Patterns (<c>*</c> and <c>?</c> wildcards) of sections or files, e.g. <c>terrain</c>, <c>features</c> or
/// <c>features/09-*</c>; none selects every file.
/// </param>
/// <param name="Cases">
/// Patterns of case names, e.g. <c>tree_oak_*</c>; none selects every case, with or without a name.
/// </param>
internal sealed record CaptureSelection(IReadOnlyList<string> Sections, IReadOnlyList<string> Cases)
{
    // MSBuild splits -p values at commas and semicolons, so lists separate their patterns with '+' (or spaces).
    private static readonly char[] Separators = ['+', ',', ';', ' '];

    public bool IsEverything => this.Sections.Count == 0 && this.Cases.Count == 0;

    /// <summary>Reads <c>--sections &lt;patterns&gt;</c> and <c>--cases &lt;patterns&gt;</c>.</summary>
    public static CaptureSelection Parse(IReadOnlyList<string> options)
    {
        var sections = new List<string>();
        var cases = new List<string>();
        for (var i = 0; i < options.Count; i += 2)
        {
            if (i + 1 == options.Count)
                throw new ArgumentException($"{options[i]} needs a value.");

            var patterns = options[i + 1].Split(Separators, StringSplitOptions.RemoveEmptyEntries);
            var list = options[i] switch
            {
                "--sections" => sections,
                "--cases" => cases,
                _ => throw new ArgumentException($"Unknown option {options[i]}; use --sections or --cases."),
            };
            list.AddRange(patterns);
        }

        return new CaptureSelection(sections, cases);
    }

    public bool Includes(WorldgenFixtureFile file) =>
        this.Sections.Count == 0 || this.Sections.Any(pattern => Selects(pattern, file));

    public bool Includes(JsonNode entry)
    {
        if (this.Cases.Count == 0)
            return true;

        var name = entry["name"]?.GetValue<string>();
        return name is not null && this.Cases.Any(pattern => Matches(pattern, name));
    }

    /// <summary>The patterns that select nothing in <paramref name="files"/>, which are likely typos.</summary>
    public IEnumerable<string> Unmatched(IReadOnlyList<WorldgenFixtureFile> files)
    {
        var included = files.Where(this.Includes).ToList();
        var names = included
            .SelectMany(file => file.Cases)
            .Select(entry => entry!["name"]?.GetValue<string>())
            .OfType<string>()
            .ToList();

        var sections = this.Sections.Where(pattern => !files.Any(file => Selects(pattern, file)));
        var cases = this.Cases.Where(pattern => !names.Any(name => Matches(pattern, name)));
        return sections.Concat(cases);
    }

    public override string ToString()
    {
        var sections = this.Sections.Count == 0 ? "every section" : "sections " + string.Join(" + ", this.Sections);
        var cases = this.Cases.Count == 0 ? "every case" : "cases " + string.Join(" + ", this.Cases);
        return $"{sections}, {cases}";
    }

    private static bool Selects(string pattern, WorldgenFixtureFile file) =>
        Matches(pattern, file.Section) || Matches(pattern, file.Name);

    private static bool Matches(string pattern, string value) =>
        FileSystemName.MatchesSimpleExpression(pattern, value, ignoreCase: false);
}
