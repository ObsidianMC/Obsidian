import com.google.gson.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.function.*;
import net.minecraft.core.HolderGetter;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.level.levelgen.NoiseGeneratorSettings;
import net.minecraft.world.level.levelgen.RandomState;
import net.minecraft.world.level.levelgen.densityfunction.DensityFunction;
import net.minecraft.world.level.levelgen.synth.NormalNoise;

/**
 * Re-records the vanilla worldgen references of Obsidian's 26.3 parity tests
 * (Obsidian.Tests/Assets/vanilla_worldgen_26.3.json); WorldgenCaptures.cs runs it (docs/vanilla-captures.md). Run it in
 * an empty directory with the server's class path and {@code --add-opens java.base/java.lang=ALL-UNNAMED}, twice per
 * world. First {@code <fixture json> <seed> <generate structures> <version> create} creates and saves the world, then
 * the caller deletes its chunks ({@code region}, {@code entities} and {@code poi} folders), then
 * {@code <fixture json> <seed> <generate structures> <version>} captures. Worlds without cases exit right away.
 *
 * <p>It runs a headless vanilla dedicated server in this JVM for its registries and runs every case on the server
 * thread. Only the fixture's cases for this world are recaptured, and the fixture is rewritten in place.
 *
 * <p>Phase 5 of docs/porting-26.3-plan.md adds sections step by step. The 1.21.11 probe's chunk, structure, light and
 * spawn captures (in git history, before Phase 5) come back as the terrain, feature and structure steps adapt them.
 */
class WorldgenCaptures {
    // Which world (structures on or off) each section's cases are captured in.
    static final Map<String, Boolean> SECTIONS = new LinkedHashMap<>();
    static {
        SECTIONS.put("noises", false);
        SECTIONS.put("router", false);
    }

    static MinecraftServer server;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        long seed = Long.parseLong(args[1]);
        boolean structures = Boolean.parseBoolean(args[2]);
        var version = args[3];
        boolean create = args.length > 4 && args[4].equals("create");

        JsonObject fixture;
        try (var reader = Files.newBufferedReader(fixturePath, StandardCharsets.UTF_8)) {
            fixture = JsonParser.parseReader(reader).getAsJsonObject();
        }

        var cases = casesOf(fixture, seed, structures);
        if (cases.isEmpty()) {
            System.out.println("No cases for seed " + seed + " with structures " + structures + ".");
            return;
        }

        if (create) {
            createWorld(seed, structures);
        }

        var report = new ArrayList<String>();
        try {
            startServer(seed, structures);

            for (var section : cases.entrySet()) {
                for (var entry : section.getValue()) {
                    var before = entry.deepCopy();
                    capture(section.getKey(), entry);
                    report.add(compare(section.getKey(), before, entry));
                }
            }
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }

        fixture.addProperty("version", version);
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(fixturePath, gson.toJson(fixture) + "\n", StandardCharsets.UTF_8);

        System.out.println("Captured seed " + seed + " with structures " + structures + ":");
        report.forEach(System.out::println);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    // The fixture's cases for this world, by section.
    static Map<String, List<JsonObject>> casesOf(JsonObject fixture, long seed, boolean structures) {
        var cases = new LinkedHashMap<String, List<JsonObject>>();

        for (var section : SECTIONS.entrySet()) {
            if (section.getValue() != structures || !fixture.has(section.getKey())) {
                continue;
            }

            for (var element : fixture.getAsJsonArray(section.getKey())) {
                var entry = element.getAsJsonObject();
                if (entry.get("seed").getAsLong() == seed) {
                    cases.computeIfAbsent(section.getKey(), name -> new ArrayList<>()).add(entry);
                }
            }
        }

        return cases;
    }

    // Creates the world, which picks its spawn (generating the chunks around it), and saves it. The caller then
    // deletes the world's chunks, so that captures start without any.
    static void createWorld(long seed, boolean structures) {
        try {
            startServer(seed, structures);
            onServer(() -> server.saveEverything(true, true, true));

            System.out.println("Created the world for seed " + seed + " with structures " + structures + ".");
            System.out.flush();
            Runtime.getRuntime().halt(0);
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }
    }

    // One line per case: the case's inputs, then whether its captured values are what the fixture held.
    static String compare(String section, JsonObject before, JsonObject after) {
        var changes = new ArrayList<String>();

        for (var property : after.entrySet()) {
            var old = before.get(property.getKey());
            if (old == null || !sameValue(old, property.getValue())) {
                changes.add(property.getKey() + " " + old + " -> " + property.getValue());
            }
        }

        var outcome = changes.isEmpty() ? ": unchanged" : ": CHANGED " + String.join(", ", changes);
        return "  " + section + " " + before + outcome;
    }

    // Numbers compare by value, since the fixture may spell them differently (64 or 64.0).
    static boolean sameValue(JsonElement a, JsonElement b) {
        var bothNumbers = a.isJsonPrimitive()
            && b.isJsonPrimitive()
            && a.getAsJsonPrimitive().isNumber()
            && b.getAsJsonPrimitive().isNumber();

        if (!bothNumbers) {
            return a.equals(b);
        }

        var first = new java.math.BigDecimal(a.getAsString());
        var second = new java.math.BigDecimal(b.getAsString());
        return first.compareTo(second) == 0;
    }

    static void capture(String section, JsonObject entry) {
        try {
            onServer(() -> {
                switch (section) {
                    case "noises" -> noise(entry);
                    case "router" -> router(entry);
                    default -> throw new IllegalArgumentException(section);
                }
                return null;
            });
        } catch (Exception e) {
            throw new RuntimeException("Capturing " + section + " " + entry + " failed", e);
        }
    }

    // ---------------------------------------------------------------- server

    static void startServer(long seed, boolean structures) throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");

        // No watchdog, since a capture runs on the server thread for as long as it takes, and no pausing while nobody
        // is online.
        var properties = String.join("\n",
            "level-seed=" + seed,
            "generate-structures=" + structures,
            "online-mode=false",
            "server-port=0",
            "max-tick-time=-1",
            "pause-when-empty-seconds=-1",
            "spawn-protection=0",
            "sync-chunk-writes=false",
            "view-distance=2",
            "simulation-distance=2",
            "enable-jmx-monitoring=false",
            "enable-rcon=false",
            "enable-query=false",
            "");
        Files.writeString(Path.of("server.properties"), properties);

        net.minecraft.server.Main.main(new String[]{"--nogui"});

        // Main keeps the server only in its shutdown hook (which halts it), so find it there.
        var hooksField = Class.forName("java.lang.ApplicationShutdownHooks").getDeclaredField("hooks");
        hooksField.setAccessible(true);
        @SuppressWarnings("unchecked")
        var hooks = (Map<Thread, Thread>) hooksField.get(null);

        for (var hook : new ArrayList<>(hooks.keySet())) {
            if (hook.getClass().getName().equals("net.minecraft.server.Main$1")) {
                var field = hook.getClass().getDeclaredField("val$dedicatedServer");
                field.setAccessible(true);
                server = (MinecraftServer) field.get(hook);
            }
        }

        if (server == null) {
            throw new IllegalStateException("The dedicated server didn't start.");
        }

        while (!server.isReady()) {
            Thread.sleep(50);
        }
    }

    static <T> T onServer(Supplier<T> work) {
        return server.submit(work).join();
    }

    // ---------------------------------------------------------------- captures

    static HolderGetter<NormalNoise> noises() {
        return server.registryAccess().lookupOrThrow(Registries.NOISE);
    }

    // Vanilla: RandomState.create(noises, seed, settings), as the chunk generator creates it for the dimension.
    static RandomState randomState(JsonObject entry, NoiseGeneratorSettings settings) {
        return RandomState.create(noises(), entry.get("seed").getAsLong(), settings);
    }

    static NoiseGeneratorSettings settings(JsonObject entry) {
        var id = Identifier.parse(entry.get("settings").getAsString());
        var key = ResourceKey.create(Registries.NOISE_SETTINGS, id);

        return server.registryAccess().lookupOrThrow(Registries.NOISE_SETTINGS).getOrThrow(key).value();
    }

    // Vanilla: RandomState.getOrCreateNoise(noise).get(x, y, z), a 26.3 noise at a point, with the overworld's random.
    static void noise(JsonObject entry) {
        var overworld = server.registryAccess()
            .lookupOrThrow(Registries.NOISE_SETTINGS)
            .getOrThrow(NoiseGeneratorSettings.OVERWORLD)
            .value();
        var random = randomState(entry, overworld);

        var id = Identifier.parse(entry.get("noise").getAsString());
        var noise = random.getOrCreateNoise(ResourceKey.create(Registries.NOISE, id));
        var x = entry.get("x").getAsDouble();
        var y = entry.get("y").getAsDouble();
        var z = entry.get("z").getAsDouble();

        entry.addProperty("value", noise.get(x, y, z));
    }

    // Vanilla: RandomState.sampleBlockValueUncached(function, x, y, z) for each router function and, where the
    // dimension has aquifers, each aquifer function, by their JSON field names.
    static void router(JsonObject entry) {
        var settings = settings(entry);
        var random = randomState(entry, settings);
        var router = settings.noiseRouter();
        var values = new JsonObject();

        BiConsumer<String, DensityFunction> sample = (name, function) -> {
            var value = random.sampleBlockValueUncached(
                function,
                entry.get("x").getAsInt(),
                entry.get("y").getAsInt(),
                entry.get("z").getAsInt());
            values.addProperty(name, value);
        };

        sample.accept("temperature", router.temperature());
        sample.accept("vegetation", router.vegetation());
        sample.accept("continents", router.continents());
        sample.accept("erosion", router.erosion());
        sample.accept("depth", router.depth());
        sample.accept("ridges", router.ridges());
        sample.accept("chunk_surface_level", router.chunkSurfaceLevel());
        sample.accept("final_density", router.finalDensity());

        settings.aquifers().ifPresent(aquifers -> {
            sample.accept("aquifers.barrier", aquifers.barrierNoise());
            sample.accept("aquifers.fluid_level_floodedness", aquifers.fluidLevelFloodednessNoise());
            sample.accept("aquifers.fluid_level_spread", aquifers.fluidLevelSpreadNoise());
            sample.accept("aquifers.lava", aquifers.lavaNoise());
            sample.accept("aquifers.exclusion", aquifers.exclusion());
            sample.accept("aquifers.surface_level", aquifers.surfaceLevel());
        });

        entry.add("values", values);
    }
}
