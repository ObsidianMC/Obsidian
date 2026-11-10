namespace Obsidian.AssetGenerator;

/// <summary>Captures parity fixtures with the same server and installed-client class paths as the asset dumpers.</summary>
internal static class FixtureDumper
{
    private static string Source(string name) => Path.Combine(AppContext.BaseDirectory, "Java", name);

    internal static Task DumpCopperWeatheringAsync(VanillaServer server, string fixture) =>
        VanillaServer.RunJavaAsync(server.ScratchDirectory, "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            Source("CopperWeatheringFixtures.java"), fixture);

    internal static Task DumpSavedHeightmapsAsync(VanillaServer server, string fixture)
    {
        var world = Path.Combine(server.ScratchDirectory, $"saved-heightmaps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(world);
        var source = Path.Combine(AppContext.BaseDirectory, "Captures", "SavedHeightmapFixtures.java");
        return VanillaServer.RunJavaAsync(world, TimeSpan.FromMinutes(5),
            "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), source, fixture);
    }

    internal static Task DumpHeightmapsAsync(VanillaServer server, string fixture)
    {
        var world = Path.Combine(server.ScratchDirectory, $"heightmaps-{Guid.NewGuid():N}");
        Directory.CreateDirectory(world);
        return VanillaServer.RunJavaAsync(world, TimeSpan.FromMinutes(5),
            "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("HeightmapFixtures.java"), fixture);
    }

    internal static async Task DumpMoreBlocksAsync(VanillaServer server, string fixture,
        IReadOnlyCollection<string>? cases = null)
    {
        var root = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(fixture))!;
        var selected = root["cases"]!.AsArray()
            .Where(probe => cases is null || cases.Contains(probe!["name"]!.GetValue<string>()))
            .ToArray();
        if (cases is not null && selected.Length != cases.Count)
            throw new ArgumentException("Every selected case must exist and be named once.");

        var observers = selected
            .Where(probe => probe!["kind"]!.GetValue<string>() == "observer_placement")
            .Select(probe => probe!["name"]!.GetValue<string>())
            .ToArray();
        if (observers.Length > 0)
        {
            var world = Path.Combine(server.ScratchDirectory, $"observer-placement-{Guid.NewGuid():N}");
            Directory.CreateDirectory(world);
            await VanillaServer.RunJavaAsync(world,
                "--add-opens", "java.base/java.lang=ALL-UNNAMED",
                "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("PlacementFixtures.java"),
                fixture, "--observers", string.Join('+', observers));
        }

        var bounded = selected
            .Where(probe => probe!["kind"]!.GetValue<string>() != "observer_placement")
            .Select(probe => probe!["name"]!.GetValue<string>())
            .ToArray();
        if (bounded.Length == 0 && observers.Length > 0)
            return;

        var arguments = new List<string>
        {
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("MoreBlockFixtures.java"), fixture, "-"
        };
        if (cases is not null || observers.Length > 0)
        {
            arguments.Add(string.Join('+', bounded));
        }

        await VanillaServer.RunJavaAsync(server.ScratchDirectory, arguments.ToArray());
    }

    // An on-request gameplay probe: it does not regenerate unrelated assets or fixtures.
    internal static Task DumpMiningAsync(VanillaServer server, string fixture) =>
        VanillaServer.RunJavaAsync(server.ScratchDirectory, "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            Source("MiningFixtures.java"), fixture);

    // An on-request gameplay probe: fire's tick and placement on a bounded block view. With cases, only those are
    // recorded.
    internal static Task DumpFireAsync(VanillaServer server, string fixture, IReadOnlyCollection<string>? cases = null)
    {
        var arguments = new List<string>
        {
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("FireFixtures.java"), fixture
        };
        if (cases is not null)
            arguments.Add(string.Join('+', cases));

        return VanillaServer.RunJavaAsync(server.ScratchDirectory, arguments.ToArray());
    }

    // An on-request gameplay probe: vanilla's block ticks, block changes and landings against each case's block view.
    internal static Task DumpTickingBlocksAsync(
        VanillaServer server, string fixture, IReadOnlyCollection<string>? cases = null)
    {
        var arguments = new List<string>
        {
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("TickingBlockFixtures.java"), fixture
        };
        if (cases is not null)
            arguments.Add(string.Join('+', cases));

        return VanillaServer.RunJavaAsync(server.ScratchDirectory, arguments.ToArray());
    }

    // An on-request gameplay probe: turtle eggs' random tick and placement, with a seeded random.
    internal static Task DumpTurtleEggsAsync(VanillaServer server, string fixture) =>
        VanillaServer.RunJavaAsync(server.ScratchDirectory, "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            Source("HatchingFixtures.java"), fixture);

    // An on-request gameplay probe: frogspawn's placement and hatch tick, with a seeded random.
    internal static Task DumpFrogspawnAsync(VanillaServer server, string fixture) =>
        VanillaServer.RunJavaAsync(server.ScratchDirectory, "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            Source("HatchingFixtures.java"), fixture);

    // An on-request gameplay probe: eyeblossoms, potted eyeblossoms, dried ghasts and nether portals ticking against
    // each case's block view.
    internal static Task DumpEyeblossomGhastPortalAsync(
        VanillaServer server, string fixture, IReadOnlyCollection<string>? cases = null)
    {
        var arguments = new List<string>
        {
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("EyeblossomGhastPortalFixtures.java"),
            fixture
        };
        if (cases is not null)
            arguments.Add(string.Join('+', cases));

        return VanillaServer.RunJavaAsync(server.ScratchDirectory, arguments.ToArray());
    }

    // An on-request gameplay probe: vanilla's randomTick against each case's block view, with a seeded random.
    internal static Task DumpGrowingPlantTicksAsync(VanillaServer server, string fixture) =>
        VanillaServer.RunJavaAsync(server.ScratchDirectory, "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            Source("GrowingPlantTickFixtures.java"), fixture);

    // An on-request gameplay probe: what cooking machines give a player taking their result, or drop when broken.
    internal static Task DumpFurnaceResultsAsync(VanillaServer server, string fixture) =>
        VanillaServer.RunJavaAsync(server.ScratchDirectory, "-cp", string.Join(Path.PathSeparator, server.ClassPath),
            Source("FurnaceFixtures.java"), fixture);

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

    // An on-request probe: a dedicated server of its own, in a new flat world under the scratch directory.
    internal static Task DumpPlacementAsync(VanillaServer server, string fixture)
    {
        var world = Path.Combine(server.ScratchDirectory, "block-placement");
        if (Directory.Exists(world))
            Directory.Delete(world, recursive: true);

        Directory.CreateDirectory(world);

        // The probe finds the server in the shutdown hook vanilla's Main registers.
        return VanillaServer.RunJavaAsync(world,
            "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("PlacementFixtures.java"), fixture);
    }

    // An on-request probe: a dedicated server of its own, in a new flat world under the scratch directory.
    internal static Task DumpBlockUseAsync(VanillaServer server, string fixture,
        IReadOnlyCollection<string>? cases = null) =>
        RunObservedProbeAsync(server, fixture, "block-use", "BlockUseFixtures.java", cases);

    // An on-request probe: a dedicated server of its own, in a new flat world under the scratch directory. It reuses
    // BlockUseFixtures.java from the same directory (the source launcher compiles it on demand).
    internal static Task DumpWaxingBellsOreAsync(VanillaServer server, string fixture,
        IReadOnlyCollection<string>? cases = null) =>
        RunObservedProbeAsync(server, fixture, "waxing-bells-ore", "WaxingBellsOreFixtures.java", cases);

    // Runs a probe on an observed copy of the vanilla jar, which records ordered block writes, in a new world folder
    // named worldName under the scratch directory.
    private static async Task RunObservedProbeAsync(VanillaServer server, string fixture, string worldName,
        string probe, IReadOnlyCollection<string>? cases)
    {
        var world = Path.Combine(server.ScratchDirectory, worldName);
        if (Directory.Exists(world))
            Directory.Delete(world, recursive: true);

        Directory.CreateDirectory(world);

        var observed = Path.Combine(world, "observed-classes");
        await VanillaServer.RunJavaAsync(world,
            "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("BlockUseInstrumentation.java"),
            server.ClassPath[0], observed);
        var observedJar = Path.Combine(observed, "vanilla-observed.jar");
        var classPath = string.Join(Path.PathSeparator, new[] { observedJar }.Concat(server.ClassPath.Skip(1)));

        // The probe finds the server in the shutdown hook vanilla's Main registers.
        await VanillaServer.RunJavaAsync(world,
            "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "-cp", classPath, Source(probe), fixture,
            cases is null ? "" : string.Join('+', cases));
    }

    // On-request probes: a dedicated server of its own each, in a flat world in a temporary folder that is deleted
    // afterwards, so no capture world is left in the checkout.
    internal static Task DumpBrushingAsync(VanillaServer server, string fixture,
        IReadOnlyCollection<string>? cases = null) =>
        RunInTemporaryWorldAsync(server, "BrushingFixtures.java", fixture, cases);

    internal static Task DumpArmorStandUseAsync(VanillaServer server, string fixture,
        IReadOnlyCollection<string>? cases = null) =>
        RunInTemporaryWorldAsync(server, "ArmorStandFixtures.java", fixture, cases);

    private static async Task RunInTemporaryWorldAsync(VanillaServer server, string probe, string fixture,
        IReadOnlyCollection<string>? cases)
    {
        var world = Path.Combine(Path.GetTempPath(), $"obsidian-capture-{Guid.NewGuid():N}");
        Directory.CreateDirectory(world);

        try
        {
            // The probe finds the server in the shutdown hook vanilla's Main registers.
            await VanillaServer.RunJavaAsync(world,
                "--add-opens", "java.base/java.lang=ALL-UNNAMED",
                "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source(probe), Path.GetFullPath(fixture),
                cases is null ? "" : string.Join('+', cases));
        }
        finally
        {
            Directory.Delete(world, recursive: true);
        }
    }

    // An on-request probe: a dedicated server of its own, in a new normal world under the scratch directory.
    internal static async Task DumpMeltingWeatherAsync(VanillaServer server, string fixture,
        IReadOnlyCollection<string>? cases = null)
    {
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(fixture));
        var selected = document.RootElement.GetProperty("cases").EnumerateArray()
            .Where(probe => cases is null || cases.Contains(probe.GetProperty("name").GetString()!))
            .ToArray();
        if (cases is not null && selected.Length != cases.Count)
            throw new ArgumentException("Every selected case must exist and be named once.");

        // Flat and noise generators supply different sea levels. Each group gets its own native server/world.
        var groups = selected.GroupBy(probe => probe.TryGetProperty("generator", out var generator)
            ? generator.GetString()! : "normal");
        foreach (var group in groups)
        {
            var world = Path.Combine(server.ScratchDirectory, $"melting-weather-{Guid.NewGuid():N}");
            Directory.CreateDirectory(world);
            var names = string.Join('+', group.Select(probe => probe.GetProperty("name").GetString()));
            await VanillaServer.RunJavaAsync(world,
                "--add-opens", "java.base/java.lang=ALL-UNNAMED",
                "-cp", string.Join(Path.PathSeparator, server.ClassPath), Source("MeltingWeatherFixtures.java"),
                fixture, names, group.Key);
        }
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
