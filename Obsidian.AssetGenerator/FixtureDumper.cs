namespace Obsidian.AssetGenerator;

/// <summary>Captures parity fixtures with the same server and installed-client class paths as the asset dumpers.</summary>
internal static class FixtureDumper
{
    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, "Java", name);

    public static async Task DumpServerAsync(VanillaServer server, string output)
    {
        var classpath = string.Join(Path.PathSeparator, server.ClassPath);
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", classpath, Source("ComponentFixtures.java"),
            server.MappingsPath, Source("component-fixtures-input.json"), Path.Combine(output, $"item-components-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", classpath, Source("EntityMetadataFixtures.java"),
            server.MappingsPath, Path.Combine(output, $"client-entity-metadata-{server.Version}.json"));
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", classpath, Source("ScreenFixtures.java"),
            server.MappingsPath, Path.Combine(output, $"client-screens-{server.Version}.json"));
        await DumpBlockLootAsync(server, classpath, output);
    }

    // The block loot dumper is several source files, so it's compiled first (javac finds its sources through -sourcepath).
    // The launcher loads the compiled classes with its own class loader, which avoids a signer conflict with the server's jars.
    private static async Task DumpBlockLootAsync(VanillaServer server, string classpath, string output)
    {
        var classes = Path.Combine(server.Directory, "block-loot-classes");
        if (Directory.Exists(classes))
            Directory.Delete(classes, recursive: true);
        Directory.CreateDirectory(classes);

        await VanillaServer.RunJavacAsync(server.Directory, "-nowarn", "-d", classes, "-cp", classpath,
            "-sourcepath", Path.GetDirectoryName(Source("BlockLootFixtures.java"))!, Source("BlockLootFixtures.java"));
        // The vanilla loot tables the dumper reads are generated into the server's data directory.
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", classpath, Source("BlockLootLauncher.java"), classes,
            server.MappingsPath, Source("block-loot-fixture-input.json"),
            Path.Combine(server.GeneratedDirectory, "data", "minecraft", "loot_table", "blocks"),
            Path.Combine(output, $"block-loot-fixtures-{server.Version}.json"));
    }

    public static async Task DumpClientAsync(VanillaServer server, IReadOnlyList<string> classpath, string mappings, string output)
    {
        await VanillaServer.RunJavaAsync(server.Directory, "-cp", string.Join(Path.PathSeparator, classpath),
            Source("ClientProtocolFixtures.java"), mappings, Path.Combine(output, $"client-protocol-{server.Version}.json"));
        var fixtures = new[] { ("HudFixtures", "hud"), ("ParticleFixtures", "particles"), ("PlayerPhysicsFixtures", "player-physics") };
        foreach (var (name, fixture) in fixtures)
        {
            // Only our probe source is name-resolved. No game jar is rewritten or downloaded.
            var source = Path.Combine(server.Directory, "fixture-sources", name + ".java");
            await VanillaServer.RunJavaAsync(server.Directory, "-cp", string.Join(Path.PathSeparator, classpath),
                Source("FixtureSource.java"), mappings, Source(name + ".java.template"), source);
            await VanillaServer.RunJavaAsync(server.Directory, "-Djava.awt.headless=true", "-cp", string.Join(Path.PathSeparator, classpath),
                source, Path.Combine(output, $"client-{fixture}-{server.Version}.json"));
        }
    }
}
