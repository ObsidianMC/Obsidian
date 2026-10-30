using Obsidian.API.Configuration;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Obsidian.Hosting;

/// <summary>Writes the default <c>config/server.json</c> and <c>config/whitelist.json</c> of a fresh install.</summary>
public static class ConfigFiles
{
    /// <summary>
    /// Writes the configuration files that don't exist yet. A new <c>server.json</c> states vanilla's whitelist default
    /// explicitly; an existing one without a <c>whitelist</c> entry keeps the old default (off), which
    /// <paramref name="notice"/> reports once.
    /// </summary>
    public static async ValueTask GenerateAsync(string path, Action<string> notice)
    {
        Directory.CreateDirectory(path);

        var serverJsonFile = Path.Combine(path, "server.json");
        var whitelistJsonFile = Path.Combine(path, "whitelist.json");

        if (!File.Exists(serverJsonFile))
            await WriteConfigFile(serverJsonFile, ServerConfiguration.CreateDefault());
        else if (!await SetsWhitelistAsync(serverJsonFile))
            notice($"{serverJsonFile} doesn't set \"whitelist\", so the whitelist stays off; add it to choose.");

        if (!File.Exists(whitelistJsonFile))
            await WriteConfigFile(whitelistJsonFile, new WhitelistConfiguration());
    }

    private static async ValueTask<bool> SetsWhitelistAsync(string path)
    {
        var options = new JsonDocumentOptions
        {
            CommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };

        try
        {
            await using var file = File.OpenRead(path);
            using var document = await JsonDocument.ParseAsync(file, options);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return true;

            const string key = nameof(ServerConfiguration.Whitelist);
            var names = document.RootElement.EnumerateObject().Select(property => property.Name);
            return names.Any(name => name.Equals(key, StringComparison.OrdinalIgnoreCase));
        }
        catch (JsonException)
        {
            // The configuration loader reports a malformed file itself.
            return true;
        }
    }

    private static async ValueTask WriteConfigFile<T>(string path, T configuration)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            IgnoreReadOnlyProperties = true
        };

        var schemaFile = Path.GetFileName(path);
        var json = new JsonObject
        {
            ["$schema"] = $"https://raw.githubusercontent.com/ObsidianMC/Obsidian/master/.schema/{schemaFile}"
        };

        foreach (var property in JsonSerializer.SerializeToNode(configuration, options)!.AsObject())
            json.Add(property.Key, property.Value?.DeepClone());

        if (configuration is ServerConfiguration)
        {
            json["Logging"] = new JsonObject
            {
                ["LogLevel"] = new JsonObject
                {
                    ["Default"] = "Information",
                    ["Microsoft"] = "Warning"
                }
            };
        }

        await using var file = File.Create(path);
        await JsonSerializer.SerializeAsync(file, json, options);
    }
}
