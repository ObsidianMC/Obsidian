using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;

namespace Obsidian.Tests;

/// <summary>
/// The vanilla worldgen and structure references the parity tests replay: the 1.21.11 references of the legacy
/// generator (<see cref="VanillaWorldGeneration"/>, <see cref="VanillaStructureGeneration"/>) from
/// <c>Assets/vanilla_worldgen.json</c>, and the 26.3 references (<see cref="VanillaDensityFunctions"/>) from
/// <c>Assets/vanilla_worldgen_26.3</c>.
/// </summary>
/// <remarks>
/// A fixture holds each case's inputs and the values vanilla produced for them. Obsidian.AssetGenerator re-records
/// the 26.3 values in place with <c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen</c>, which runs
/// vanilla's own world generation (see <c>docs/vanilla-captures.md</c>); add a case by adding its inputs and
/// re-recording. The 26.3 fixture is a folder with a file per section (<c>noises.json</c>) or a folder of files per
/// section (<c>features/02-ores.json</c>), each holding <c>{ "version": ..., "cases": [...] }</c>; a section's cases are
/// its files' cases in ordinal order of their names, so a capture can rewrite one file alone. Phase 5 of <c>docs/porting-26.3-plan.md</c> moves the 1.21.11 sections to the 26.3 fixture as the new
/// generator replaces the legacy one.
/// </remarks>
internal static class VanillaWorldgenCaptures
{
    private static readonly Lazy<JsonElement> Fixture = new(() => Load("vanilla_worldgen.json"));
    private const string CurrentFixtureFolder = "vanilla_worldgen_26.3";

    private static readonly Lazy<Dictionary<string, List<JsonElement>>> CurrentFixture = new(LoadCurrent);

    /// <summary>The cases of a section of the 1.21.11 fixture, in its order.</summary>
    public static IEnumerable<JsonElement> Cases(string section) => Fixture.Value.GetProperty(section).EnumerateArray();

    /// <summary>The cases of a section of the 26.3 fixture, in its order.</summary>
    public static IEnumerable<JsonElement> CurrentCases(string section) => CurrentFixture.Value[section];

    /// <summary>The case of a section for a seed's chunk.</summary>
    public static JsonElement Chunk(string section, long seed, int chunkX, int chunkZ) => Cases(section).Single(entry =>
        entry.GetProperty("seed").GetInt64() == seed && entry.GetProperty("chunkX").GetInt32() == chunkX &&
        entry.GetProperty("chunkZ").GetInt32() == chunkZ);

    private static JsonElement Load(string name) => LoadResource($"Obsidian.Tests.Assets.{name}");

    private static JsonElement LoadResource(string resource)
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)!;
        using var document = JsonDocument.Parse(stream);

        return document.RootElement.Clone();
    }

    // The 26.3 fixture's sections. Its files are embedded as Obsidian.Tests.Assets.vanilla_worldgen_26.3/<path>, with
    // the path separators of the OS that built the tests.
    private static Dictionary<string, List<JsonElement>> LoadCurrent()
    {
        var prefix = $"Obsidian.Tests.Assets.{CurrentFixtureFolder}/";
        var files = Assembly.GetExecutingAssembly().GetManifestResourceNames()
            .Where(resource => resource.StartsWith(prefix, StringComparison.Ordinal))
            .Select(resource => (Resource: resource, Path: resource[prefix.Length..].Replace('\\', '/')))
            .OrderBy(file => file.Path, StringComparer.Ordinal);

        var sections = new Dictionary<string, List<JsonElement>>();
        foreach (var (resource, path) in files)
        {
            var section = path.Split('/')[0].Replace(".json", string.Empty, StringComparison.Ordinal);
            var cases = LoadResource(resource).GetProperty("cases").EnumerateArray();

            if (!sections.TryGetValue(section, out var list))
                sections[section] = list = [];

            list.AddRange(cases);
        }

        return sections;
    }
}
