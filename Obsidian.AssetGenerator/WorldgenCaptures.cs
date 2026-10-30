using System.Text.Json;

namespace Obsidian.AssetGenerator;

/// <summary>
/// Re-records the 26.3 vanilla worldgen references the parity tests replay
/// (<c>Obsidian.Tests/Assets/vanilla_worldgen_26.3.json</c>), by running <c>Captures/WorldgenCaptures.java</c> against
/// the vanilla server. See <c>docs/vanilla-captures.md</c>.
/// </summary>
/// <remarks>
/// The capture runs a headless vanilla server per world (seed, with and without structures), which evaluates every
/// case with vanilla's own code. It is slow and needs no rebuild of the assets, so it only runs on request
/// (<c>dotnet build Obsidian.AssetGenerator -t:CaptureVanillaWorldgen</c>), not on every build.
/// </remarks>
internal static class WorldgenCaptures
{
    // A world's server can hang rather than exit when it fails (the probe waits for it to become ready and for chunks to
    // load), so each run is killed after this long. A whole capture takes about a minute.
    private static readonly TimeSpan WorldTimeout = TimeSpan.FromMinutes(15);

    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, name);

    public static async Task RunAsync(VanillaServer server, string fixturePath)
    {
        // Each world rewrites its own cases, so they're captured into a copy that replaces the fixture only once every
        // world succeeded. A failed capture never leaves a fixture mixing versions.
        var capturing = fixturePath + ".capturing";
        File.Copy(fixturePath, capturing, overwrite: true);
        try
        {
            await CaptureWorldsAsync(server, fixturePath, capturing);
            File.Move(capturing, fixturePath, overwrite: true);
        }
        finally
        {
            File.Delete(capturing);
        }
    }

    private static async Task CaptureWorldsAsync(VanillaServer server, string fixturePath, string capturing)
    {
        var classpath = string.Join(Path.PathSeparator, server.ClassPath);
        var source = Source(Path.Combine("Captures", "WorldgenCaptures.java"));

        foreach (var seed in Seeds(fixturePath))
        {
            foreach (var structures in new[] { false, true })
            {
                var world = Path.Combine(server.Directory, "captures", $"{seed}-{(structures ? "structures" : "no-structures")}");
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
                    foreach (var chunks in Directory.EnumerateDirectories(world, folder, SearchOption.AllDirectories).ToList())
                        Directory.Delete(chunks, recursive: true);
                }

                await VanillaServer.RunJavaAsync(world, WorldTimeout, arguments);
                Directory.Delete(world, recursive: true);
            }
        }
    }

    // The seeds of the fixture's cases, each captured in a world of its own.
    private static List<long> Seeds(string fixturePath)
    {
        using var fixture = JsonDocument.Parse(File.ReadAllText(fixturePath));
        return fixture.RootElement.EnumerateObject()
            .Where(section => section.Value.ValueKind == JsonValueKind.Array)
            .SelectMany(section => section.Value.EnumerateArray())
            .Select(entry => entry.GetProperty("seed").GetInt64())
            .Distinct()
            .Order()
            .ToList();
    }
}
