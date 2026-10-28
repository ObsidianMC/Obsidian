namespace Obsidian.AssetGenerator;

/// <summary>Captures parity fixtures with the same server and installed-client class paths as the asset dumpers.</summary>
internal static class FixtureDumper
{
    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, "Java", name);

    public static async Task DumpServerAsync(VanillaServer server, string output)
    {
        var classpath = string.Join(Path.PathSeparator, server.ClassPath);
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", classpath, Source("ComponentFixtures.java"),
            Source("component-fixtures-input.json"), Path.Combine(output, $"item-components-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", classpath, Source("EntityMetadataFixtures.java"),
            Path.Combine(output, $"client-entity-metadata-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", classpath, Source("ScreenFixtures.java"),
            Path.Combine(output, $"client-screens-{server.Version}.json"));
    }

    public static async Task DumpClientAsync(VanillaServer server, IReadOnlyList<string> classpath, string output)
    {
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", string.Join(Path.PathSeparator, classpath),
            Source("ClientProtocolFixtures.java"), Path.Combine(output, $"client-protocol-{server.Version}.json"));
        var fixtures = new[] { ("HudFixtures", "hud"), ("ParticleFixtures", "particles"), ("PlayerPhysicsFixtures", "player-physics") };
        foreach (var (name, fixture) in fixtures)
        {
            await VanillaServer.RunJavaAsync(server.Directory, "-Djava.awt.headless=true", "-cp", string.Join(Path.PathSeparator, classpath),
                Source(name + ".java"), Path.Combine(output, $"client-{fixture}-{server.Version}.json"));
        }
    }
}
