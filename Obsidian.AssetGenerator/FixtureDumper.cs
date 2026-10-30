namespace Obsidian.AssetGenerator;

/// <summary>Captures parity fixtures with the same server and installed-client class paths as the asset dumpers.</summary>
internal static class FixtureDumper
{
    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, "Java", name);

    public static async Task DumpServerAsync(VanillaServer server, string output)
    {
        var classpath = string.Join(Path.PathSeparator, server.ClassPath);
        var scratch = server.ScratchDirectory;
        await VanillaServer.RunJavaAsync(
            scratch,
            "-cp",
            classpath,
            Source("ContextNumberFixtures.java"),
            Source("context-number-cases.json"),
            Path.Combine(output, $"context-numbers-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("ComponentFixtures.java"),
            Source("component-fixtures-input.json"), Path.Combine(output, $"item-components-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("EntityMetadataFixtures.java"),
            Path.Combine(output, $"client-entity-metadata-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("ScreenFixtures.java"),
            Path.Combine(output, $"client-screens-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("MovementFixtures.java"),
            Path.Combine(output, $"client-movement-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("RemainingPlayFixtures.java"),
            Path.Combine(output, $"remaining-play-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("BrewingFixtures.java"),
            Path.Combine(server.GeneratedDirectory, "data"), Path.Combine(output, $"brewing-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("SpecialRecipeFixtures.java"),
            Path.Combine(server.GeneratedDirectory, "data"),
            Path.Combine(output, $"special-recipes-{server.Version}.json"));
        // Also writes synced_registries.json, which the server reads at runtime.
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("JoinFixtures.java"),
            Path.Combine(output, $"join-payloads-{server.Version}.json"), Path.Combine(output, "synced_registries.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("DataSystemsFixtures.java"),
            Path.Combine(output, $"data-systems-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("TradeFixtures.java"),
            Path.Combine(output, $"villager-trades-{server.Version}.json"));
    }

    public static async Task DumpClientAsync(VanillaServer server, IReadOnlyList<string> classpath, string output)
    {
        var scratch = server.ScratchDirectory;
        await VanillaServer.RunJavaAsync(scratch, "-cp", string.Join(Path.PathSeparator, classpath),
            Source("ClientProtocolFixtures.java"), Path.Combine(output, $"client-protocol-{server.Version}.json"));
        var fixtures = new[] { ("HudFixtures", "hud"), ("ParticleFixtures", "particles"), ("PlayerPhysicsFixtures", "player-physics") };
        foreach (var (name, fixture) in fixtures)
        {
            await VanillaServer.RunJavaAsync(scratch, "-Djava.awt.headless=true",
                "-cp", string.Join(Path.PathSeparator, classpath),
                Source(name + ".java"), Path.Combine(output, $"client-{fixture}-{server.Version}.json"));
        }
    }
}
