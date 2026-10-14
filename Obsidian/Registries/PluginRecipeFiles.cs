using Microsoft.Extensions.Logging;
using Obsidian.API.Crafting;
using Obsidian.Plugins;
using System.IO;
using System.Text.Json;

namespace Obsidian.Registries;

/// <summary>Reads vanilla-shaped recipe files after item tags and plugin recipe serializers are registered.</summary>
internal static class PluginRecipeFiles
{
    internal static ImmutableArray<RecipeFile> Read(DataFiles files, ILogger logger)
    {
        var result = ImmutableArray.CreateBuilder<RecipeFile>();
        foreach (var (path, identifier) in FindFiles(files.Paths))
        {
            try
            {
                var bytes = files.Read(path) ?? throw new InvalidDataException($"{path} can't be read.");
                if (bytes.Length > files.Budget.Available(path))
                    throw new InvalidDataException($"{path} exceeds the recipe file budget.");

                files.Budget.Charge(path, bytes.Length);
                using var json = JsonDocument.Parse(bytes);
                result.Add(new RecipeFile(identifier, json.RootElement.Clone()));
            }
            catch (Exception ex) when (ex is JsonException or InvalidDataException)
            {
                logger.LogWarning(ex, "Could not read recipe {Path} from {Source}", path, files.Location);
            }
        }

        return result.ToImmutable();
    }

    internal static IEnumerable<(string Path, string Identifier)> FindFiles(IEnumerable<string> paths)
    {
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var parts = path.Split('/', 4);
            if (parts.Length == 4 && parts[0] == "data" && parts[2] == "recipe"
                && parts[3].EndsWith(".json", StringComparison.Ordinal))
            {
                var identifier = parts[1] + ":" + parts[3][..^5];
                if (PluginTags.IsFileIdentifier(identifier))
                    yield return (path, identifier);
            }
        }
    }

    internal static IRecipe[] ReadVanilla(RecipeFile file)
    {
        var json = "{" + JsonSerializer.Serialize(file.Identifier) + ":" + file.Json.GetRawText() + "}";
        return JsonSerializer.Deserialize<IRecipe[]>(json, Globals.JsonOptions)!;
    }
}

internal sealed record RecipeFile(string Identifier, JsonElement Json);
