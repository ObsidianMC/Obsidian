namespace Obsidian.AssetGenerator;

/// <summary>Captures parity fixtures with the same server and installed-client class paths as the asset dumpers.</summary>
internal static class FixtureDumper
{
    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, "Java", name);

    internal static Task DumpHeightmapsAsync(VanillaServer server, string fixture)
    {
        var world = Path.Combine(server.ScratchDirectory, $"heightmaps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(world);
        return VanillaServer.RunJavaAsync(world, TimeSpan.FromMinutes(5),
            "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("HeightmapFixtures.java"), fixture);
    }

    internal static Task DumpMoreBlocksAsync(VanillaServer server, string fixture,
        IReadOnlyCollection<string>? cases = null)
    {
        var arguments = new List<string>
        {
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("MoreBlockFixtures.java"), fixture, "-"
        };
        if (cases is not null)
        {
            arguments.Add(string.Join('+', cases));
        }

        return VanillaServer.RunJavaAsync(server.ScratchDirectory, arguments.ToArray());
    }

    // An on-request gameplay probe: it does not regenerate unrelated assets or fixtures.
    internal static Task DumpSulfurAsync(VanillaServer server, string fixture) =>
        VanillaServer.RunJavaAsync(server.ScratchDirectory, "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            Source("SulfurFixtures.java"), fixture);

    // An on-request probe: a dedicated server of its own, in a new world under the scratch directory.
    internal static Task DumpStructurePiecesAsync(VanillaServer server, string fixture,
        IReadOnlyCollection<string>? cases = null)
    {
        var world = Path.Combine(server.ScratchDirectory, "structure-pieces");
        if (Directory.Exists(world))
            Directory.Delete(world, recursive: true);

        Directory.CreateDirectory(world);

        var arguments = new List<string>
        {
            // The probe finds the server in the shutdown hook vanilla's Main registers.
            "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("StructurePieceFixtures.java"), fixture
        };
        if (cases is not null)
            arguments.Add(string.Join('+', cases));

        return VanillaServer.RunJavaAsync(world, arguments.ToArray());
    }

    public static async Task DumpServerAsync(VanillaServer server, string output)
    {
        var classpath = string.Join(Path.PathSeparator, server.ClassPath);
        var scratch = server.ScratchDirectory;
        await VanillaServer.RunJavaAsync(scratch, "-cp", classpath, Source("CommandFixtures.java"),
            Path.Combine(output, $"commands-{server.Version}.json"));
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
