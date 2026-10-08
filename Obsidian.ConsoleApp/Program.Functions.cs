using Obsidian.API.Configuration;
using System.Text.Json;
using System.Text.Json.Nodes;

public partial class Program
{
    private static async ValueTask GenerateConfigFiles()
    {
        const string path = "config";

        Directory.CreateDirectory(path);

        var serverJsonFile = Path.Combine(path, "server.json");
        var whitelistJsonFile = Path.Combine(path, "whitelist.json");

        if (!File.Exists(serverJsonFile))
        {
            await WriteConfigFile(serverJsonFile, new ServerConfiguration());
        }

        if (!File.Exists(whitelistJsonFile))
        {
            await WriteConfigFile(whitelistJsonFile, new WhitelistConfiguration());
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

        var json = new JsonObject
        {
            ["$schema"] = $"https://raw.githubusercontent.com/ObsidianMC/Obsidian/master/.schema/{Path.GetFileName(path)}"
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
