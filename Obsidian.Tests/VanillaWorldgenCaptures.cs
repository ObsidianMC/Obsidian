using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Tests;

/// <summary>
/// The vanilla worldgen and structure references <see cref="VanillaWorldGeneration"/> and
/// <see cref="VanillaStructureGeneration"/> replay, from <c>Assets/vanilla_worldgen.json</c>.
/// </summary>
/// <remarks>
/// The fixture holds each case's inputs and the values vanilla produced for them. Obsidian.AssetGenerator re-records the
/// values in place with <c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen</c>, which runs vanilla's own
/// world generation (see <c>docs/vanilla-captures.md</c>); add a case by adding its inputs and re-recording.
/// </remarks>
internal static class VanillaWorldgenCaptures
{
    private static readonly Lazy<JsonElement> Fixture = new(() =>
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("Obsidian.Tests.Assets.vanilla_worldgen.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    });

    /// <summary>The cases of a section of the fixture, in its order.</summary>
    public static IEnumerable<JsonElement> Cases(string section) => Fixture.Value.GetProperty(section).EnumerateArray();

    /// <summary>The case of a section for a seed's chunk.</summary>
    public static JsonElement Chunk(string section, long seed, int chunkX, int chunkZ) => Cases(section).Single(entry =>
        entry.GetProperty("seed").GetInt64() == seed && entry.GetProperty("chunkX").GetInt32() == chunkX &&
        entry.GetProperty("chunkZ").GetInt32() == chunkZ);
}
