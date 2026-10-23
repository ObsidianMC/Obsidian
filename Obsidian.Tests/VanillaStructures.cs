using Microsoft.Extensions.Logging.Abstractions;
using Obsidian.Registries;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;

namespace Obsidian.Tests;

/// <summary>
/// Gives the tests vanilla's structure templates, which Obsidian doesn't ship: they're extracted from Mojang's server jar
/// into a cache that test runs share, so only the first run downloads the jar.
/// </summary>
internal static class VanillaStructures
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        using var httpClient = new HttpClient();
        var cache = Path.Combine(Path.GetTempPath(), "obsidian-tests", "cache");
        var directory = VanillaServerJar.ExtractStructuresAsync(httpClient, cache, ServerConstants.ProtocolDescription, NullLogger.Instance)
            .GetAwaiter().GetResult();

        StructureRegistry.Initialize(directory);
    }
}
