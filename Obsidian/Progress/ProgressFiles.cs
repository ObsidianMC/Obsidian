using Microsoft.Extensions.Logging;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.Progress;

/// <summary>Writes progress through a same-directory replacement so interrupted saves leave readable JSON.</summary>
internal static class ProgressFiles
{
    public static JsonObject Read(string path, ILogger logger)
    {
        if (!File.Exists(path))
            return new JsonObject();

        try
        {
            return JsonNode.Parse(File.ReadAllText(path)) as JsonObject
                ?? throw new InvalidDataException("Expected a progress JSON object.");
        }
        catch (Exception ex) when (ex is IOException or JsonException)
        {
            logger.LogWarning(ex, "Could not read progress {Path}", path);
            return new JsonObject();
        }
    }

    public static async Task WriteAsync(string path, JsonObject data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, data.ToJsonString(new JsonSerializerOptions
            {
                WriteIndented = true
            }));
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            File.Delete(temporary);
        }
    }
}
