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
