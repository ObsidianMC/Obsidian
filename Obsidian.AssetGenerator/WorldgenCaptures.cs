using System.Text.Json.Nodes;

namespace Obsidian.AssetGenerator;

/// <summary>
/// Re-records the 26.3 vanilla worldgen references the parity tests replay
/// (<c>Obsidian.Tests/Assets/vanilla_worldgen_26.3</c>, see <see cref="WorldgenFixtureFile"/>), by running
/// <c>Captures/WorldgenCaptures.java</c> against the vanilla server. See <c>docs/vanilla-captures.md</c>.
/// </summary>
/// <remarks>
/// The capture runs a headless vanilla server per world (seed, with and without structures), which evaluates every
/// case with vanilla's own code. It is slow and needs no rebuild of the assets, so it only runs on request
/// (<c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen</c>), not on every build. It can record only some
/// sections, files or cases (<see cref="CaptureSelection"/>), and only rewrites the files holding them.
/// </remarks>
internal static class WorldgenCaptures
{
    // A world's server can hang rather than exit when it fails (the probe waits for it to become ready and for chunks to
    // load), so each run is killed after this long. A whole capture takes a few minutes.
    private static readonly TimeSpan WorldTimeout = TimeSpan.FromMinutes(15);

    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, name);

    public static async Task RunAsync(VanillaServer server, string fixtureDirectory, CaptureSelection selection)
    {
        var files = WorldgenFixtureFile.Load(fixtureDirectory);
        var unmatched = selection.Unmatched(files).ToList();
        if (unmatched.Count > 0)
            throw new ArgumentException($"Nothing in {fixtureDirectory} matches {string.Join(", ", unmatched)}.");

        // The probe captures the selected cases in a fixture of their own, by section, which replaces them in their
        // files only once every world succeeded. A failed capture never leaves files mixing versions.
        var selected = Select(files, selection);
        var capturing = Path.Combine(server.ScratchDirectory, "captures", "fixture.json");
        Directory.CreateDirectory(Path.GetDirectoryName(capturing)!);
        Console.WriteLine($"Capturing {selected.Count} cases ({selection})...");
        // Completion runs with one worker to fix neighboring feature write order. Give it its own server so
        // that setting cannot alter the scheduling or biome-search history of existing capture sections.
        foreach (var group in selected.GroupBy(entry => entry.File.Section == "completion"))
        {
            var batch = group.ToList();
            await File.WriteAllTextAsync(capturing, WorldgenFixtureFile.Serialize(Fixture(batch)));
            await CaptureWorldsAsync(server, capturing, Seeds(batch));

            var captured = JsonNode.Parse(await File.ReadAllTextAsync(capturing))!.AsObject();
            Replace(batch, captured);
            File.Delete(capturing);
        }

        foreach (var file in selected.Select(entry => entry.File).Distinct())
        {
            file.Root["version"] = server.Version;
            var outcome = file.Save() ? "Rewrote" : "Unchanged:";
            Console.WriteLine($"{outcome} {file.Name}.json");
        }
    }

    // The selected cases, in the files' order.
    private static List<SelectedCase> Select(List<WorldgenFixtureFile> files, CaptureSelection selection)
    {
        var selected = new List<SelectedCase>();
        foreach (var file in files.Where(selection.Includes))
        {
            for (var index = 0; index < file.Cases.Count; index++)
            {
                if (selection.Includes(file.Cases[index]!))
                    selected.Add(new SelectedCase(file, index));
            }
        }

        if (selected.Count == 0)
            throw new ArgumentException($"No cases match {selection}.");

        return selected;
    }

    // The probe's input: the selected cases by section, in the files' order.
    private static JsonObject Fixture(List<SelectedCase> selected)
    {
        var fixture = new JsonObject();
        foreach (var (file, index) in selected)
        {
            if (fixture[file.Section] is not JsonArray section)
                fixture[file.Section] = section = new JsonArray();

            section.Add(file.Cases[index]!.DeepClone());
        }

        return fixture;
    }

    // Puts the probe's captured cases back in their files, at their positions.
    private static void Replace(List<SelectedCase> selected, JsonObject captured)
    {
        var positions = new Dictionary<string, int>();
        foreach (var (file, index) in selected)
        {
            var position = positions.GetValueOrDefault(file.Section);
            positions[file.Section] = position + 1;

            file.Cases[index] = captured[file.Section]![position]!.DeepClone();
        }
    }

    private static async Task CaptureWorldsAsync(VanillaServer server, string capturing, List<long> seeds)
    {
        var classpath = string.Join(Path.PathSeparator, server.ClassPath);
        var source = Source(Path.Combine("Captures", "WorldgenCaptures.java"));

        foreach (var seed in seeds)
        {
            foreach (var structures in new[] { false, true })
            {
                var worldName = $"{seed}-{(structures ? "structures" : "no-structures")}";
                var world = Path.Combine(server.ScratchDirectory, "captures", worldName);
                if (Directory.Exists(world))
                    Directory.Delete(world, recursive: true);
                Directory.CreateDirectory(world);

                Console.WriteLine($"Capturing seed {seed} with structures {(structures ? "on" : "off")}...");
                string[] arguments =
                [
                    "-Xmx4G", "--add-opens", "java.base/java.lang=ALL-UNNAMED", "-cp", classpath, source,
                    capturing, seed.ToString(), structures ? "true" : "false", server.Version
                ];

                // Creating the world picks its spawn, which generates chunks around it. Captures start from the saved
                // world without any chunks, so that each case generates its chunks itself, in its own order.
                await VanillaServer.RunJavaAsync(world, WorldTimeout, [.. arguments, "create"]);
                foreach (var folder in new[] { "region", "entities", "poi" })
                {
                    var chunkFolders = Directory.EnumerateDirectories(world, folder, SearchOption.AllDirectories);
                    foreach (var chunks in chunkFolders.ToList())
                        Directory.Delete(chunks, recursive: true);
                }

                await VanillaServer.RunJavaAsync(world, WorldTimeout, arguments);
                Directory.Delete(world, recursive: true);
            }
        }
    }

    // The seeds of the selected cases, each captured in a world of its own.
    private static List<long> Seeds(List<SelectedCase> selected) =>
        [.. selected
            .Select(entry => entry.File.Cases[entry.Index]!["seed"]!.GetValue<long>())
            .Distinct()
            .Order()];

    private readonly record struct SelectedCase(WorldgenFixtureFile File, int Index);
}
