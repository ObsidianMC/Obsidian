using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.Registries;
using Obsidian.WorldData.Structures;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;

namespace Obsidian.Tests;

/// <summary>
/// 26.3 templates for feature captures, isolated from the legacy structure generator's template cache.
/// </summary>
internal static class VanillaFeatureTemplates
{
    private static readonly Lazy<string> directory = new(Extract);
    private static readonly ConcurrentDictionary<string, StructureTemplate> templates = new();

    public static StructureTemplate Get(string id) => templates.GetOrAdd(id, Load);

    private static string Extract()
    {
        using var client = new HttpClient();
        var cache = Path.Combine(Path.GetTempPath(), "obsidian-tests", "cache");

        return VanillaServerJar.ExtractStructuresAsync(client, cache, "26.3", NullLogger.Instance)
            .GetAwaiter().GetResult();
    }

    private static StructureTemplate Load(string id)
    {
        var path = Path.Combine(directory.Value, id["minecraft:".Length..] + ".nbt");

        if (!File.Exists(path))
        {
            return StructureTemplate.CreateEmpty();
        }

        using var stream = File.OpenRead(path);
        return StructureTemplate.Load(stream);
    }
}
