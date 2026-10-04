using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.AssetGenerator.Datagen;

/// <summary>
/// Reads the data generators' JSON files and writes the assets combined from them.
/// </summary>
internal static class DatagenJson
{
    private static readonly JsonWriterOptions writerOptions = new()
    {
        Indented = true,
        // Keeps text components and descriptions readable instead of escaping every non-ASCII character.
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static JsonNode Read(string path) => JsonNode.Parse(File.ReadAllText(path))
        ?? throw new InvalidDataException($"{path} is null.");

    public static JsonObject ReadObject(string path) => Read(path).AsObject();

    public static void WriteJson(string path, JsonNode node)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var stream = File.Create(path);
        using var writer = new Utf8JsonWriter(stream, writerOptions);
        node.WriteTo(writer);
    }

    /// <summary>
    /// Reads every JSON file under <paramref name="directory"/>, keyed by its path relative to the directory without
    /// the extension (<c>adventure/root</c>).
    /// </summary>
    /// <remarks>
    /// Ordered ordinally by that path, which is the order vanilla lists the resources of a registry in (sorted by
    /// resource location) and so the order its datapack registries assign ids in.
    /// </remarks>
    public static List<(string Path, JsonNode Json)> ReadAll(string directory) =>
    [
        .. Directory.EnumerateFiles(directory, "*.json", SearchOption.AllDirectories)
            .Select(file => Path.GetRelativePath(directory, file).Replace('\\', '/')[..^".json".Length])
            .Order(StringComparer.Ordinal)
            .Select(path => (path, Read(Path.Combine(directory, path + ".json"))))
    ];

    /// <summary>
    /// Combines every JSON file under <paramref name="directory"/> into one object keyed by <paramref name="key"/> of
    /// each file's relative path (see <see cref="ReadAll"/>).
    /// </summary>
    public static JsonObject Combine(string directory, Func<string, string> key)
    {
        var combined = new JsonObject();
        foreach (var (path, json) in ReadAll(directory))
            combined[key(path)] = json;

        return combined;
    }

    /// <summary>Copies every file under <paramref name="source"/> to <paramref name="destination"/> as is.</summary>
    public static void CopyDirectory(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = Path.Combine(destination, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    /// <summary>
    /// Every string value of a property in <paramref name="node"/> and its descendants, with the property's name.
    /// </summary>
    public static IEnumerable<(string Property, string Value)> StringProperties(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                foreach (var (name, value) in obj)
                {
                    if (value is JsonValue jsonValue && jsonValue.TryGetValue(out string? text))
                        yield return (name, text);
                    else
                        foreach (var nested in StringProperties(value))
                            yield return nested;
                }
                break;
            case JsonArray array:
                foreach (var item in array)
                    foreach (var nested in StringProperties(item))
                        yield return nested;
                break;
        }
    }
}
