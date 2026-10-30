import com.google.gson.*;
import com.mojang.serialization.JsonOps;
import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.function.*;
import java.util.zip.GZIPOutputStream;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.HolderGetter;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.RegistryOps;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.biome.BiomeManager;
import net.minecraft.world.level.biome.BiomeResolver;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.*;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.levelgen.*;
import net.minecraft.world.level.levelgen.blending.Blender;
import net.minecraft.world.level.levelgen.densityfunction.DensityFunction;
import net.minecraft.world.level.levelgen.densityfunction.DensityVolume;
import net.minecraft.world.level.levelgen.material.MaterialRuleContext;
import net.minecraft.world.level.levelgen.material.rule.MaterialRule;
import net.minecraft.world.level.levelgen.placement.FeaturePlacer;
import net.minecraft.server.level.WorldGenRegion;
import net.minecraft.util.StaticCache2D;
import net.minecraft.world.attribute.EnvironmentAttributeSystem;
import net.minecraft.world.level.chunk.status.ChunkPyramid;
import net.minecraft.world.level.chunk.status.ChunkStep;
import net.minecraft.world.ticks.WorldGenTickAccess;
import net.minecraft.world.level.levelgen.synth.NormalNoise;

/**
 * Re-records the vanilla worldgen references of Obsidian's 26.3 parity tests
 * (Obsidian.Tests/Assets/vanilla_worldgen_26.3); WorldgenCaptures.cs runs it (docs/vanilla-captures.md). The fixture
 * json it reads and rewrites holds the cases to record by section ({@code {"features": [...], ...}}), which the C#
 * driver gathers from the fixture's files and puts back in them, so it records only those. Run it in
 * an empty directory with the server's class path and {@code --add-opens java.base/java.lang=ALL-UNNAMED}, twice per
 * world. First {@code <fixture json> <seed> <generate structures> <version> create} creates and saves the world, then
 * the caller deletes its chunks ({@code region}, {@code entities} and {@code poi} folders), then
 * {@code <fixture json> <seed> <generate structures> <version>} captures. Worlds without cases exit right away.
 *
 * <p>It runs a headless vanilla dedicated server in this JVM for its registries and runs every case on the server
 * thread. Only the fixture's cases for this world are recaptured, and the fixture json is rewritten in place.
 *
 * <p>Phase 5 of docs/porting-26.3-plan.md adds sections step by step. Terrain, materials, single features,
 * biome sources, biome decoration, spawn attributes and structures are captured here. Lighting follows later.
 */
class WorldgenCaptures {
    // Which world (structures on or off) each section's cases are captured in.
    static final Map<String, Boolean> SECTIONS = new LinkedHashMap<>();
    static {
        SECTIONS.put("noises", false);
        SECTIONS.put("router", false);
        SECTIONS.put("functions", false);
        SECTIONS.put("biomes", false);
        SECTIONS.put("terrain", false);
        SECTIONS.put("materials", false);
        SECTIONS.put("aquifers", false);
        SECTIONS.put("carvers", false);
        SECTIONS.put("features", false);
        SECTIONS.put("decoration", false);
        SECTIONS.put("biome_search", false);
        SECTIONS.put("biome_points", false);
        SECTIONS.put("spawn_attributes", false);
        SECTIONS.put("structures", true);
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
            preflightFeatures(cases.getOrDefault("features", List.of()));

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
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().serializeNulls().create();
        Files.writeString(fixturePath, gson.toJson(fixture) + "\n", StandardCharsets.UTF_8);

        System.out.println("Captured seed " + seed + " with structures " + structures + ":");
        report.forEach(System.out::println);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    // Reject malformed new inputs before running the expensive terrain and region probes.
    static void preflightFeatures(List<JsonObject> entries) {
        var ops = RegistryOps.create(JsonOps.INSTANCE, server.registryAccess());
        var errors = new ArrayList<String>();
        for (var entry : entries) {
            try {
                var kind = entry.has("probe") ? entry.get("probe").getAsString() : "placement";
                switch (kind) {
                    case "provider" -> net.minecraft.world.level.levelgen.feature.stateproviders.BlockStateProvider.CODEC
                        .parse(ops, entry.get("provider")).getOrThrow();
                    case "modifier" -> net.minecraft.world.level.levelgen.placement.PlacementModifier.CODEC
                        .parse(ops, entry.get("modifier")).getOrThrow();
                    case "predicate" -> net.minecraft.world.level.levelgen.blockpredicates.BlockPredicate.CODEC
                        .parse(ops, entry.get("predicate")).getOrThrow();
                    case "rule" -> net.minecraft.world.level.levelgen.structure.templatesystem.RuleTest.CODEC
                        .parse(ops, entry.get("rule")).getOrThrow();
                    default -> net.minecraft.world.level.levelgen.placement.PlacedFeature.CODEC
                        .parse(ops, entry.get("feature")).getOrThrow();
                }
            } catch (RuntimeException exception) {
                errors.add(entry.get("name") + ": " + exception.getMessage());
            }
        }
        if (!errors.isEmpty()) {
            throw new IllegalArgumentException(String.join("\n", errors));
        }
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
                changes.add(property.getKey() + " " + brief(old) + " -> " + brief(property.getValue()));
            }
        }

        var outcome = changes.isEmpty() ? ": unchanged" : ": CHANGED " + String.join(", ", changes);
        var inputs = before.deepCopy();
        if (section.equals("terrain") || section.equals("biomes") || section.equals("decoration")) {
            inputs = new JsonObject();
            for (var field : List.of("seed", "dimension", "chunkX", "chunkZ")) {
                inputs.add(field, before.get(field));
            }
        }
        if (section.equals("features")) {
            inputs = new JsonObject();
            for (var field : List.of("seed", "feature", "biome", "chunkX", "chunkZ")) {
                inputs.add(field, before.get(field));
            }
        }
        if (section.equals("biome_search")) {
            inputs = new JsonObject();
            var name = before.has("preset") ? before.get("preset") : before.get("name");
            inputs.add("name", name);
        }
        return "  " + section + " " + inputs + outcome;
    }

    static String brief(JsonElement value) {
        if (value == null) {
            return "null";
        }
        if (value.isJsonObject() && value.getAsJsonObject().has("hash")) {
            return value.getAsJsonObject().get("hash").toString();
        }
        var text = value.toString();
        return text.length() <= 160 ? text : "[" + text.length() + " characters]";
    }

    // Numbers compare by value, since the fixture may spell them differently (64 or 64.0), also inside objects such as
    // a router case's values.
    static boolean sameValue(JsonElement a, JsonElement b) {
        if (a.isJsonObject() && b.isJsonObject()) {
            return sameObject(a.getAsJsonObject(), b.getAsJsonObject());
        }

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

    static boolean sameObject(JsonObject a, JsonObject b) {
        if (!a.keySet().equals(b.keySet())) {
            return false;
        }

        for (var key : a.keySet()) {
            if (!sameValue(a.get(key), b.get(key))) {
                return false;
            }
        }

        return true;
    }

    static void capture(String section, JsonObject entry) {
        try {
            onServer(() -> {
                switch (section) {
                    case "noises" -> noise(entry);
                    case "router" -> router(entry);
                    case "functions" -> function(entry);
                    case "terrain" -> terrain(entry);
                    case "materials" -> materialRule(entry);
                    case "aquifers" -> aquifer(entry);
                    case "carvers" -> carvers(entry);
                    case "features" -> feature(entry);
                    case "decoration" -> decoration(entry);
                    case "biomes" -> biomeSections(entry);
                    case "biome_search" -> biomeSearch(entry);
                    case "biome_points" -> biomePoint(entry);
                    case "spawn_attributes" -> spawnAttributes(entry);
                    case "structures" -> structures(entry);
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

    static NoiseGeneratorSettings overworld() {
        return server.registryAccess()
            .lookupOrThrow(Registries.NOISE_SETTINGS)
            .getOrThrow(NoiseGeneratorSettings.OVERWORLD)
            .value();
    }

    // Vanilla: RandomState.getOrCreateNoise(noise).get(x, y, z), a 26.3 noise at a point, with the overworld's random.
    static void noise(JsonObject entry) {
        var random = randomState(entry, overworld());

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

    // Vanilla: DensityFunction.CODEC parses the case's inline function, then
    // RandomState.sampleBlockValueUncached(function, x, y, z) samples it with the overworld's random. Besides the
    // value, it records Float.floatToIntBits of it in hex, since JSON numbers hold neither NaN nor infinities.
    static void function(JsonObject entry) {
        var random = randomState(entry, overworld());
        var ops = RegistryOps.create(JsonOps.INSTANCE, server.registryAccess());
        var function = DensityFunction.CODEC.parse(ops, entry.get("function")).getOrThrow();

        var value = random.sampleBlockValueUncached(
            function,
            entry.get("x").getAsInt(),
            entry.get("y").getAsInt(),
            entry.get("z").getAsInt());

        entry.addProperty("value", Float.toString(value));
        entry.addProperty("bits", String.format("0x%08X", Float.floatToIntBits(value)));
    }

    // Terrain is captured at its actual saved status. Separate calls to vanilla's own private stages expose the
    // density, aquifer and material boundaries, so a replay can diagnose the first differing stage.
    static void terrain(JsonObject entry) {
        try {
            terrainChecked(entry);
        } catch (Exception exception) {
            throw new RuntimeException(exception);
        }
    }

    static void terrainChecked(JsonObject entry) throws Exception {
        var level = level(entry.get("dimension").getAsString());
        var generator = (NoiseBasedChunkGenerator) level.getChunkSource().getGenerator();
        var settings = generator.generatorSettings().value();
        var random = level.getChunkSource().randomState();
        int chunkX = entry.get("chunkX").getAsInt();
        int chunkZ = entry.get("chunkZ").getAsInt();
        var position = new ChunkPos(chunkX, chunkZ);
        var chunk = new ProtoChunk(
            position,
            UpgradeData.EMPTY,
            level,
            PalettedContainerFactory.create(server.registryAccess()),
            null);
        var biomeSource = generator.getBiomeSource();
        var resolver = biomeSource.createUncachedResolver(random);
        var manager = new BiomeManager(resolver, BiomeManager.obfuscateSeed(entry.get("seed").getAsLong()));
        var fluidPicker = fluidPicker(settings);
        int minY = settings.noiseSettings().minY();
        int height = settings.noiseSettings().height();
        var volume = new DensityVolume(16, height, 16, chunkX * 16, minY, chunkZ * 16);

        int buildMinY = chunk.getMinY();
        int buildHeight = chunk.getHeight();
        entry.addProperty("minY", buildMinY);
        entry.addProperty("height", buildHeight);
        entry.addProperty("settings", generator.generatorSettings().unwrapKey().orElseThrow().identifier().toString());
        JsonObject stagedBlocks;
        try (var noiseChunk = new NoiseChunk(
            random,
            Beardifier.EMPTY,
            settings,
            fluidPicker,
            Blender.empty(),
            volume)) {
            var finalDensity = noiseChunk.cachingSamplers().get(settings.noiseRouter().finalDensity());
            try (var density = finalDensity.sampleVolume(volume)) {
                var bytes = new ByteArrayOutputStream();
                var output = new DataOutputStream(bytes);
                for (int index = 0; index < volume.size(); index++) {
                    output.writeInt(Float.floatToIntBits(density.get(index)));
                }
                entry.addProperty("density", hexHash(bytes.toByteArray()));
                entry.addProperty("densityData", compressed(bytes.toByteArray()));
            }

            invokeStage(generator, "doFill", noiseChunk, chunk);
            entry.add("base", blocks(chunk, buildMinY, buildHeight));
            entry.add("baseHeightmaps", heights(chunk));
            invokeStage(generator, "buildSurface", chunk, noiseChunk, random, manager,
                biomeSource.possibleBiomes(), settings.materialRule().value());
            entry.add("materials", blocks(chunk, buildMinY, buildHeight));
            var seed = entry.get("seed").getAsLong();
            entry.addProperty("carvingMask", carvingMask(seed, chunk, generator, resolver, volume));

            if (settings.aquifers().isPresent()) {
                var fluids = new JsonArray();
                var aquifer = noiseChunk.aquifer();
                int sampleX = chunkX * 16 + 7;
                int sampleZ = chunkZ * 16 + 9;
                for (int y : List.of(-48, -8, 24, 56, 96)) {
                    var fluid = computeFluid(aquifer, sampleX, y, sampleZ);
                    var sample = new JsonObject();
                    sample.addProperty("x", sampleX);
                    sample.addProperty("y", y);
                    sample.addProperty("z", sampleZ);
                    sample.addProperty("level", fluid.fluidLevel());
                    sample.addProperty("fluid", fluidName(fluid));
                    fluids.add(sample);
                }
                entry.add("aquiferFluids", fluids);
            } else {
                entry.remove("aquiferFluids");
            }

            invokeStage(generator, "generateCarvers", chunk, Blender.empty(), noiseChunk, random, manager,
                null, settings.materialRule().value());
            stagedBlocks = blocks(chunk, buildMinY, buildHeight);
        }

        var actual = level.getChunkSource().getChunk(chunkX, chunkZ, ChunkStatus.TERRAIN, true);
        if (actual.getPersistedStatus() != ChunkStatus.TERRAIN) {
            throw new IllegalStateException("Expected terrain, got " + actual.getPersistedStatus());
        }
        var actualBlocks = blocks(actual, buildMinY, buildHeight);
        if (!actualBlocks.get("hash").equals(stagedBlocks.get("hash"))) {
            throw new IllegalStateException("Isolated stages differ from the terrain status at " + position);
        }
        entry.add("blocks", actualBlocks);
        entry.add("heightmaps", heights(actual));
        entry.addProperty("status", actual.getPersistedStatus().getName());
        var postProcessing = new JsonArray();
        var sections = actual.getPostProcessing();
        for (int section = 0; section < sections.length; section++) {
            if (sections[section] == null) {
                continue;
            }
            for (short packed : sections[section]) {
                var block = ProtoChunk.unpackOffsetCoordinates(packed, (buildMinY >> 4) + section, position);
                int index = (block.getY() - buildMinY) * 256 + (block.getZ() & 15) * 16 + (block.getX() & 15);
                postProcessing.add(index);
            }
        }
        entry.add("postProcessing", postProcessing);

        // Biome selection belongs to the next Phase 5 step. These are captured inputs to material rules, not an
        // alternative biome implementation: replay provides the same block biome lookup through an explicit hook.
        var biomeNames = new LinkedHashMap<String, Integer>();
        var biomeBytes = new ByteArrayOutputStream();
        var biomeOutput = new DataOutputStream(biomeBytes);
        for (int z = 0; z < 16; z++) {
            for (int x = 0; x < 16; x++) {
                for (int y = buildMinY; y <= buildMinY + buildHeight; y++) {
                    var biome = manager.getBiome(chunkX * 16 + x, y, chunkZ * 16 + z);
                    var name = biome.unwrapKey().orElseThrow().identifier().toString();
                    biomeOutput.writeInt(biomeNames.computeIfAbsent(name, key -> biomeNames.size()));
                }
            }
        }
        var biomes = new JsonObject();
        var palette = new JsonArray();
        biomeNames.keySet().forEach(palette::add);
        biomes.add("palette", palette);
        biomes.addProperty("data", compressed(biomeBytes.toByteArray()));
        entry.add("materialBiomes", biomes);

        entry.add("carverSources", carverSources(resolver, chunkX, chunkZ));
    }

    // Captures actual BIOMES status storage, independent of the later terrain/material stage.
    static void biomeSections(JsonObject entry) {
        try {
            var level = level(entry.get("dimension").getAsString());
            int chunkX = entry.get("chunkX").getAsInt();
            int chunkZ = entry.get("chunkZ").getAsInt();
            var chunk = level.getChunkSource().getChunk(chunkX, chunkZ, ChunkStatus.BIOMES, true);

            if (chunk.getPersistedStatus() != ChunkStatus.BIOMES) {
                throw new IllegalStateException("Expected biomes, got " + chunk.getPersistedStatus());
            }

            var names = new LinkedHashMap<String, Integer>();
            var bytes = new ByteArrayOutputStream();
            var output = new DataOutputStream(bytes);

            for (int y = chunk.getMinY() >> 2; y < (chunk.getMinY() + chunk.getHeight()) >> 2; y++) {
                for (int z = 0; z < 4; z++) {
                    for (int x = 0; x < 4; x++) {
                        var biome = chunk.getNoiseBiome(chunkX * 4 + x, y, chunkZ * 4 + z);
                        var name = biome.unwrapKey().orElseThrow().identifier().toString();
                        output.writeInt(names.computeIfAbsent(name, key -> names.size()));
                    }
                }
            }

            var palette = new JsonArray();
            names.keySet().forEach(palette::add);
            entry.add("palette", palette);
            entry.addProperty("data", compressed(bytes.toByteArray()));
            entry.addProperty("minY", chunk.getMinY());
            entry.addProperty("height", chunk.getHeight());
            entry.addProperty("status", chunk.getPersistedStatus().getName());

            // Natural spawn attributes use full-resolution lookups over stored sections. These can differ from
            // uncached material biomes where the point and volume climate paths round to different sides of a tie.
            BiomeResolver stored = (x, y, z) -> level.getChunkSource()
                .getChunk(x >> 2, z >> 2, ChunkStatus.BIOMES, true).getNoiseBiome(x, y, z);
            var manager = new BiomeManager(stored, BiomeManager.obfuscateSeed(entry.get("seed").getAsLong()));
            var spawnNames = new LinkedHashMap<String, Integer>();
            var spawnBytes = new ByteArrayOutputStream();
            var spawnOutput = new DataOutputStream(spawnBytes);

            for (int z = 0; z < 16; z++) {
                for (int x = 0; x < 16; x++) {
                    for (int y = chunk.getMinY(); y < chunk.getMinY() + chunk.getHeight(); y++) {
                        var biome = manager.getBiome(chunkX * 16 + x, y, chunkZ * 16 + z);
                        var name = biome.unwrapKey().orElseThrow().identifier().toString();
                        spawnOutput.writeInt(spawnNames.computeIfAbsent(name, key -> spawnNames.size()));
                    }
                }
            }

            var spawnBiomes = new JsonObject();
            var spawnPalette = new JsonArray();
            spawnNames.keySet().forEach(spawnPalette::add);
            spawnBiomes.add("palette", spawnPalette);
            spawnBiomes.addProperty("data", compressed(spawnBytes.toByteArray()));
            entry.add("spawnBiomes", spawnBiomes);
        } catch (Exception exception) {
            throw new RuntimeException(exception);
        }
    }

    // The ordered targets deliberately reuse one ParameterList: its previous leaf participates in exact ties.
    static void biomeSearch(JsonObject entry) {
        net.minecraft.world.level.biome.Climate.ParameterList<Integer> parameters;
        if (entry.has("preset")) {
            var preset = Identifier.withDefaultNamespace(entry.get("preset").getAsString());
            var original = server.registryAccess()
                .lookupOrThrow(Registries.MULTI_NOISE_BIOME_SOURCE_PARAMETER_LIST).getValue(preset).parameters();
            var values = new ArrayList<com.mojang.datafixers.util.Pair<
                net.minecraft.world.level.biome.Climate.ParameterPoint, Integer>>();
            for (int index = 0; index < original.values().size(); index++) {
                values.add(com.mojang.datafixers.util.Pair.of(original.values().get(index).getFirst(), index));
            }
            parameters = new net.minecraft.world.level.biome.Climate.ParameterList<>(values);
        } else {
            var codec = net.minecraft.world.level.biome.Climate.ParameterList.codec(
                com.mojang.serialization.Codec.INT.fieldOf("value"));
            parameters = codec.parse(JsonOps.INSTANCE, entry.get("parameters")).getOrThrow();
        }

        entry.addProperty("entryCount", parameters.values().size());
        try {
            var compressedTargets = Base64.getDecoder().decode(entry.get("targetsData").getAsString());
            var uncompressed = new java.util.zip.GZIPInputStream(
                new ByteArrayInputStream(compressedTargets)).readAllBytes();
            var input = new DataInputStream(new ByteArrayInputStream(uncompressed));
            var bytes = new ByteArrayOutputStream();
            var results = new DataOutputStream(bytes);

            while (input.available() > 0) {
                var target = new net.minecraft.world.level.biome.Climate.TargetPoint(
                    input.readInt(), input.readInt(), input.readInt(),
                    input.readInt(), input.readInt(), input.readInt());
                results.writeInt(parameters.findValue(target));
            }

            entry.addProperty("resultsData", compressed(bytes.toByteArray()));
        } catch (IOException exception) {
            throw new RuntimeException(exception);
        }
    }

    static void biomePoint(JsonObject entry) {
        var level = level(entry.get("dimension").getAsString());
        var random = level.getChunkSource().randomState();
        var context = net.minecraft.world.level.levelgen.densityfunction.SamplerContext.EMPTY_UNCACHED;
        var sampler = random.createClimateSampler(context);

        if (entry.has("erosion")) {
            var function = DensityFunction.CODEC.parse(JsonOps.INSTANCE, entry.get("erosion")).getOrThrow();
            var erosion = random.getSampler(function).bind(context);
            sampler = new net.minecraft.world.level.biome.Climate.Sampler(
                sampler.temperature(), sampler.humidity(), sampler.continentalness(), erosion,
                sampler.depth(), sampler.weirdness());
        }

        int x = entry.get("quartX").getAsInt();
        int y = entry.get("quartY").getAsInt();
        int z = entry.get("quartZ").getAsInt();
        var values = new JsonArray();
        var climate = sampler.sample(x, y, z);
        long[] coordinates = {climate.temperature(), climate.humidity(), climate.continentalness(),
            climate.erosion(), climate.depth(), climate.weirdness(), 0L};
        for (long value : coordinates) {
            values.add(value);
        }

        entry.add("climate", values);
        var source = level.getChunkSource().getGenerator().getBiomeSource();
        var biome = source.createResolver(sampler).getNoiseBiome(x, y, z);
        entry.addProperty("biome", biome.unwrapKey().orElseThrow().identifier().toString());
    }

    // Resolve the same attribute maps vanilla applies: default, dimension, biome. Custom maps exercise overlays.
    static void spawnAttributes(JsonObject entry) {
        var ops = RegistryOps.create(JsonOps.INSTANCE, server.registryAccess());
        var dimension = level(entry.get("dimension").getAsString()).dimensionType().attributes();
        var biomeId = Identifier.parse(entry.get("biome").getAsString());
        var biome = server.registryAccess().lookupOrThrow(Registries.BIOME).getValue(biomeId);
        var biomeMap = biome.getAttributes();

        if (entry.has("dimensionAttributes")) {
            dimension = net.minecraft.world.attribute.EnvironmentAttributeMap.CODEC
                .parse(ops, entry.get("dimensionAttributes")).getOrThrow();
        }
        if (entry.has("biomeAttributes")) {
            biomeMap = net.minecraft.world.attribute.EnvironmentAttributeMap.CODEC_ONLY_POSITIONAL
                .parse(ops, entry.get("biomeAttributes")).getOrThrow();
        }

        var attribute = net.minecraft.world.attribute.EnvironmentAttributes.NATURAL_MOB_SPAWNS;
        var value = dimension.applyModifier(attribute, attribute.defaultValue());
        value = biomeMap.applyModifier(attribute, value);
        entry.add("spawns", attribute.valueCodec().encodeStart(ops, value).getOrThrow());

        var probabilityAttribute =
            net.minecraft.world.attribute.EnvironmentAttributes.CREATURE_WORLD_GEN_SPAWN_PROBABILITY;
        var probability = dimension.applyModifier(probabilityAttribute, probabilityAttribute.defaultValue());
        probability = biomeMap.applyModifier(probabilityAttribute, probability);
        entry.addProperty("probability", probability);
    }

    static ServerLevel level(String dimension) {
        var key = switch (dimension) {
            case "minecraft:overworld" -> Level.OVERWORLD;
            case "minecraft:the_nether" -> Level.NETHER;
            case "minecraft:the_end" -> Level.END;
            default -> throw new IllegalArgumentException(dimension);
        };
        return server.getLevel(key);
    }

    // The union of the 17-by-17 source chunks' carvers, run by vanilla's carvers before changing any blocks. It uses
    // the density buffers' encoding, with 0 or 1 per position of the noise settings' volume.
    static String carvingMask(
        long seed,
        ProtoChunk chunk,
        NoiseBasedChunkGenerator generator,
        BiomeResolver resolver,
        DensityVolume volume) throws Exception {
        var position = chunk.getPos();
        var generation = new WorldGenerationContext(generator, chunk.getHeightAccessorForGeneration());
        int maskMinY = generation.getMinGenY() + 1;
        int maskMaxY = generation.getMinGenY() + generation.getGenDepth() - 8;
        var mask = new CarvingMask(maskMinY, maskMaxY);
        var carvingRandom = new WorldgenRandom(new LegacyRandomSource(0L));

        for (int dx = -8; dx <= 8; dx++) {
            for (int dz = -8; dz <= 8; dz++) {
                var source = new ChunkPos(position.x() + dx, position.z() + dz);
                var biome = resolver.getNoiseBiome(source.getMinBlockX() >> 2, 0, source.getMinBlockZ() >> 2);
                int index = 0;
                for (var carver : biome.value().getGenerationSettings().getCarvers()) {
                    carvingRandom.setLargeFeatureSeed(seed + index, source.x(), source.z());
                    if (carver.value().isStartChunk(carvingRandom)) {
                        carver.value().carve(generation, carvingRandom, position, source, mask);
                    }
                    index++;
                }
            }
        }

        var maskValues = new int[volume.size()];
        mask.visit((x, z, bottom, top) -> {
            for (int y = bottom; y <= top; y++) {
                maskValues[volume.indexUnchecked(x, y - volume.minBlockY(), z)] = 1;
            }
        });

        var maskBytes = new ByteArrayOutputStream();
        var maskOutput = new DataOutputStream(maskBytes);
        for (int value : maskValues) {
            maskOutput.writeInt(value);
        }
        return compressed(maskBytes.toByteArray());
    }

    // The ordered carver identifiers of the 17-by-17 source chunks' biomes, x-major.
    static JsonArray carverSources(BiomeResolver resolver, int chunkX, int chunkZ) {
        var carvers = new JsonArray();
        for (int dx = -8; dx <= 8; dx++) {
            for (int dz = -8; dz <= 8; dz++) {
                var biome = resolver.getNoiseBiome((chunkX + dx) * 4, 0, (chunkZ + dz) * 4);
                var names = new JsonArray();
                for (var carver : biome.value().getGenerationSettings().getCarvers()) {
                    names.add(carver.unwrapKey().orElseThrow().identifier().toString());
                }
                carvers.add(names);
            }
        }
        return carvers;
    }

    // Carving masks alone, for carver boundaries that need no whole terrain chunk: a mask reaching its top Y, carvers
    // crossing chunk edges and region-file borders, and distant chunks.
    static void carvers(JsonObject entry) {
        try {
            carversChecked(entry);
        } catch (Exception exception) {
            throw new RuntimeException(exception);
        }
    }

    static void carversChecked(JsonObject entry) throws Exception {
        var level = level(entry.get("dimension").getAsString());
        var generator = (NoiseBasedChunkGenerator) level.getChunkSource().getGenerator();
        var settings = generator.generatorSettings().value();
        var random = level.getChunkSource().randomState();
        int chunkX = entry.get("chunkX").getAsInt();
        int chunkZ = entry.get("chunkZ").getAsInt();
        var chunk = new ProtoChunk(
            new ChunkPos(chunkX, chunkZ),
            UpgradeData.EMPTY,
            level,
            PalettedContainerFactory.create(server.registryAccess()),
            null);
        var resolver = generator.getBiomeSource().createUncachedResolver(random);
        int minY = settings.noiseSettings().minY();
        int height = settings.noiseSettings().height();
        var volume = new DensityVolume(16, height, 16, chunkX * 16, minY, chunkZ * 16);
        var settingsId = generator.generatorSettings().unwrapKey().orElseThrow().identifier();
        var seed = entry.get("seed").getAsLong();

        entry.addProperty("minY", chunk.getMinY());
        entry.addProperty("height", chunk.getHeight());
        entry.addProperty("settings", settingsId.toString());
        entry.addProperty("carvingMask", carvingMask(seed, chunk, generator, resolver, volume));
        entry.add("carverSources", carverSources(resolver, chunkX, chunkZ));
    }

    // Single overworld aquifer positions: the block vanilla's aquifer picks there for the entry's density, whether it
    // schedules a fluid update, and the fluid status it computes for the position as an aquifer center. Each position
    // gets a fresh noise chunk for the chunk containing it, as density fill would use.
    static void aquifer(JsonObject entry) {
        try {
            aquiferChecked(entry);
        } catch (Exception exception) {
            throw new RuntimeException(exception);
        }
    }

    static void aquiferChecked(JsonObject entry) throws Exception {
        var level = server.overworld();
        var generator = (NoiseBasedChunkGenerator) level.getChunkSource().getGenerator();
        var settings = generator.generatorSettings().value();
        var random = level.getChunkSource().randomState();
        int x = entry.get("x").getAsInt();
        int y = entry.get("y").getAsInt();
        int z = entry.get("z").getAsInt();
        double density = entry.get("density").getAsDouble();
        int minY = settings.noiseSettings().minY();
        int height = settings.noiseSettings().height();
        var volume = new DensityVolume(16, height, 16, (x >> 4) * 16, minY, (z >> 4) * 16);
        var settingsId = generator.generatorSettings().unwrapKey().orElseThrow().identifier();

        entry.addProperty("minY", level.getMinY());
        entry.addProperty("height", level.getHeight());
        entry.addProperty("settings", settingsId.toString());

        var picker = fluidPicker(settings);
        try (var noiseChunk = new NoiseChunk(random, Beardifier.EMPTY, settings, picker, Blender.empty(), volume)) {
            var aquifer = noiseChunk.aquifer();
            var substance = aquifer.computeSubstance(x, y, z, density);
            var block = substance == null ? JsonNull.INSTANCE : new JsonPrimitive(stateName(substance));
            entry.add("block", block);
            entry.addProperty("scheduled", aquifer.shouldScheduleFluidUpdate());

            var fluid = computeFluid(aquifer, x, y, z);
            entry.addProperty("level", fluid.fluidLevel());
            entry.addProperty("fluid", fluidName(fluid));
        }
    }

    static Aquifer.FluidPicker fluidPicker(NoiseGeneratorSettings settings) throws Exception {
        var method = NoiseBasedChunkGenerator.class.getDeclaredMethod(
            "createFluidPicker",
            NoiseGeneratorSettings.class);
        method.setAccessible(true);
        return (Aquifer.FluidPicker) method.invoke(null, settings);
    }

    static Aquifer.FluidStatus computeFluid(Aquifer aquifer, int x, int y, int z) throws Exception {
        var method = aquifer.getClass().getDeclaredMethod("computeFluid", int.class, int.class, int.class);
        method.setAccessible(true);
        return (Aquifer.FluidStatus) method.invoke(aquifer, x, y, z);
    }

    static String fluidName(Aquifer.FluidStatus fluid) {
        return BuiltInRegistries.BLOCK.getKey(fluid.fluidType().getBlock()).toString();
    }

    static void invokeStage(Object target, String name, Object... arguments) throws Exception {
        var method = Arrays.stream(target.getClass().getDeclaredMethods())
            .filter(candidate -> candidate.getName().equals(name))
            .findFirst().orElseThrow();
        method.setAccessible(true);
        method.invoke(target, arguments);
    }

    // Palette indices are signed 32-bit big-endian values in z, x, y order, compressed with gzip. Unlike a hash
    // alone this retains every state for a useful first-mismatch message, without a multi-megabyte JSON array.
    static JsonObject blocks(ChunkAccess chunk, int minY, int height) throws Exception {
        var states = new LinkedHashMap<String, Integer>();
        var bytes = new ByteArrayOutputStream();
        var output = new DataOutputStream(bytes);
        var blockPosition = new BlockPos.MutableBlockPos();
        for (int z = 0; z < 16; z++) {
            for (int x = 0; x < 16; x++) {
                for (int y = minY; y < minY + height; y++) {
                    var state = chunk.getBlockState(blockPosition.set(chunk.getPos().getMinBlockX() + x, y,
                        chunk.getPos().getMinBlockZ() + z));
                    var name = stateName(state);
                    output.writeInt(states.computeIfAbsent(name, key -> states.size()));
                }
            }
        }
        var result = new JsonObject();
        var palette = new JsonArray();
        states.keySet().forEach(palette::add);
        result.add("palette", palette);
        result.addProperty("data", compressed(bytes.toByteArray()));
        // Hash palette and indices together, so equal shapes with different materials can't compare equal.
        var canonical = new ByteArrayOutputStream();
        canonical.write(palette.toString().getBytes(StandardCharsets.UTF_8));
        canonical.write(bytes.toByteArray());
        result.addProperty("hash", hexHash(canonical.toByteArray()));
        return result;
    }

    static JsonObject heights(ChunkAccess chunk) {
        var result = new JsonObject();
        for (var type : List.of(Heightmap.Types.WORLD_SURFACE_WG, Heightmap.Types.OCEAN_FLOOR_WG)) {
            var values = new JsonArray();
            for (int z = 0; z < 16; z++) {
                for (int x = 0; x < 16; x++) {
                    values.add(chunk.getHeight(type, x, z) + 1);
                }
            }
            result.add(type.getSerializationKey(), values);
        }
        return result;
    }

    static String compressed(byte[] bytes) throws IOException {
        var result = new ByteArrayOutputStream();
        try (var gzip = new GZIPOutputStream(result)) {
            gzip.write(bytes);
        }
        return Base64.getEncoder().encodeToString(result.toByteArray());
    }

    static String hexHash(byte[] bytes) throws Exception {
        return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(bytes));
    }

    static String stateName(BlockState state) {
        var name = BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString();
        if (state.getProperties().isEmpty()) {
            return name;
        }
        var properties = new TreeMap<String, String>();
        state.getValues().forEach(value -> properties.put(value.property().getName(), value.valueName()));
        return name + properties;
    }

    // Single-rule boundary references, evaluated by vanilla with explicit column depths and biome inputs.
    static void materialRule(JsonObject entry) {
        try {
            var settings = settings(entry);
            var random = randomState(entry, settings);
            var level = switch (entry.get("settings").getAsString()) {
                case "minecraft:nether" -> server.getLevel(Level.NETHER);
                case "minecraft:end" -> server.getLevel(Level.END);
                default -> server.overworld();
            };
            var generator = (NoiseBasedChunkGenerator) level.getChunkSource().getGenerator();
            int x = entry.get("x").getAsInt();
            int y = entry.get("y").getAsInt();
            int z = entry.get("z").getAsInt();
            var volume = new DensityVolume(1, 1, 1, x, y, z);
            var chunk = new ProtoChunk(new ChunkPos(x >> 4, z >> 4), UpgradeData.EMPTY, level,
                PalettedContainerFactory.create(server.registryAccess()), null);
            var biomeKey = ResourceKey.create(Registries.BIOME, Identifier.parse(entry.get("biome").getAsString()));
            var biome = server.registryAccess().lookupOrThrow(Registries.BIOME).getOrThrow(biomeKey);
            var picker = fluidPicker(settings);
            try (var noiseChunk = new NoiseChunk(random, Beardifier.EMPTY, settings, picker, Blender.empty(), volume)) {
                var constructor = MaterialRuleContext.class.getDeclaredConstructors()[0];
                constructor.setAccessible(true);
                Function<BlockPos, Holder<Biome>> getBiome = pos -> biome;
                var context = (MaterialRuleContext) constructor.newInstance(random.surfaceSystem(), random, volume,
                    noiseChunk.cachingSamplers(), getBiome,
                    new WorldGenerationContext(generator, chunk.getHeightAccessorForGeneration()), Set.of(biome));
                var updateXZ = MaterialRuleContext.class.getDeclaredMethod(
                    "updateXZ", int.class, int.class, int.class, int.class);
                updateXZ.setAccessible(true);
                updateXZ.invoke(context, x, z, entry.get("gradientX").getAsInt(), entry.get("gradientZ").getAsInt());
                var updateY = MaterialRuleContext.class.getDeclaredMethod(
                    "updateY", int.class, int.class, int.class, int.class);
                updateY.setAccessible(true);
                updateY.invoke(context, entry.get("above").getAsInt(), entry.get("below").getAsInt(),
                    entry.get("water").getAsInt(), y);
                var ops = RegistryOps.create(JsonOps.INSTANCE, server.registryAccess());
                var rule = MaterialRule.CODEC.parse(ops, entry.get("rule")).getOrThrow();
                var result = rule.compile(context).tryApply(x, y, z);
                entry.add("block", result == null ? JsonNull.INSTANCE : new JsonPrimitive(stateName(result)));
                entry.addProperty("surfaceDepth", context.surfaceDepth());
                entry.addProperty("secondary", context.getSurfaceSecondary());
                entry.addProperty("minSurface", context.getMinSurfaceLevel());
            }
        } catch (Exception exception) {
            throw new RuntimeException(exception);
        }
    }

    // Actual FEATURES status, structures disabled. Isolated terrain chunks keep cases independent, including
    // neighboring cases, and let the replay verify every writable chunk rather than just the center.
    static void decoration(JsonObject entry) {
        try {
            decorationChecked(entry);
        } catch (Exception exception) {
            throw new RuntimeException(exception);
        }
    }

    static void decorationChecked(JsonObject entry) throws Exception {
        System.out.println("Decorating " + entry.get("name"));
        var level = level(entry.get("dimension").getAsString());
        var generator = (NoiseBasedChunkGenerator) level.getChunkSource().getGenerator();
        var source = generator.getBiomeSource();
        var randomState = level.getChunkSource().randomState();
        if (!entry.has("chunkX")) {
            var target = entry.get("targetBiome").getAsString();
            int searchY = entry.has("searchY") ? entry.get("searchY").getAsInt() : 70;
            var found = source.findBiomeHorizontal(0, searchY, 0, 24000, 16,
                biome -> biome.unwrapKey().orElseThrow().identifier().toString().equals(target),
                new LegacyRandomSource(0L), true, randomState);
            if (found == null) {
                throw new IllegalStateException("No decoration target " + target);
            }
            entry.addProperty("chunkX", found.getFirst().getX() >> 4);
            entry.addProperty("chunkZ", found.getFirst().getZ() >> 4);
        }
        int chunkX = entry.get("chunkX").getAsInt();
        int chunkZ = entry.get("chunkZ").getAsInt();
        entry.addProperty("minY", level.getMinY());
        entry.addProperty("height", level.getHeight());
        entry.addProperty("seaLevel", generator.getSeaLevel());
        var step = ChunkPyramid.GENERATION_PYRAMID.getStepTo(ChunkStatus.FEATURES);
        var chunks = new HashMap<Long, ProtoChunk>();
        var resolver = source.createUncachedResolver(randomState);
        var manager = new BiomeManager(resolver, BiomeManager.obfuscateSeed(level.getSeed()));
        StaticCache2D<ChunkAccess> cache = StaticCache2D.create(chunkX, chunkZ,
            step.accumulatedDependencies().getRadius(), (x, z) -> {
                var chunk = new ProtoChunk(new ChunkPos(x, z), UpgradeData.EMPTY, level,
                    PalettedContainerFactory.create(server.registryAccess()), null);
                chunks.put(ChunkPos.pack(x, z), chunk);
                return chunk;
            });
        var center = chunks.get(ChunkPos.pack(chunkX, chunkZ));
        var region = region(level, cache, step, center);
        for (int z = chunkZ - 1; z <= chunkZ + 1; z++) {
            for (int x = chunkX - 1; x <= chunkX + 1; x++) {
                var chunk = chunks.get(ChunkPos.pack(x, z));
                generator.createBiomes(randomState, Blender.empty(), level.structureManager(), chunk).join();
                var settings = generator.generatorSettings().value();
                var volume = new DensityVolume(16, settings.noiseSettings().height(), 16,
                    x * 16, settings.noiseSettings().minY(), z * 16);
                try (var noise = new NoiseChunk(randomState, Beardifier.EMPTY, settings,
                    fluidPicker(settings), Blender.empty(), volume)) {
                    invokeStage(generator, "doFill", noise, chunk);
                    invokeStage(generator, "buildSurface", chunk, noise, randomState, manager,
                        source.possibleBiomes(), settings.materialRule().value());
                    invokeStage(generator, "generateCarvers", chunk, Blender.empty(), noise,
                        randomState, manager, null, settings.materialRule().value());
                }
                chunk.setPersistedStatus(ChunkStatus.TERRAIN);
                Heightmap.primeHeightmaps(chunk, ChunkStatus.FEATURES.heightmapsAfter());
            }
        }

        var possible = new JsonArray();
        source.possibleBiomes().forEach(biome ->
            possible.add(biome.unwrapKey().orElseThrow().identifier().toString()));
        entry.add("possibleBiomes", possible);
        var field = ChunkGenerator.class.getDeclaredField("featuresPerStep");
        field.setAccessible(true);
        @SuppressWarnings("unchecked")
        var steps = ((Supplier<List<net.minecraft.world.level.biome.FeatureSorter.StepFeatureData>>)
            field.get(generator)).get();
        var placedRegistry = server.registryAccess().lookupOrThrow(Registries.PLACED_FEATURE);
        var featureOrder = new JsonArray();
        for (var features : steps) {
            var ids = new JsonArray();
            features.features().forEach(feature -> ids.add(placedRegistry.getKey(feature).toString()));
            featureOrder.add(ids);
        }
        entry.add("featureOrder", featureOrder);
        var present = new TreeSet<String>();
        for (int z = chunkZ - 1; z <= chunkZ + 1; z++) {
            for (int x = chunkX - 1; x <= chunkX + 1; x++) {
                for (var section : chunks.get(ChunkPos.pack(x, z)).getSections()) {
                    section.getBiomes().getAll(biome ->
                        present.add(biome.unwrapKey().orElseThrow().identifier().toString()));
                }
            }
        }
        entry.add("biomes", new Gson().toJsonTree(present));
        generator.applyBiomeDecoration(region, center, level.structureManager().forWorldGenRegion(region));
        center.setPersistedStatus(ChunkStatus.FEATURES);
        entry.addProperty("status", center.getPersistedStatus().getName());
        var results = new JsonArray();
        for (int z = chunkZ - 1; z <= chunkZ + 1; z++) {
            for (int x = chunkX - 1; x <= chunkX + 1; x++) {
                var chunk = chunks.get(ChunkPos.pack(x, z));
                var result = new JsonObject();
                result.addProperty("chunkX", x);
                result.addProperty("chunkZ", z);
                result.add("blocks", blocks(chunk, level.getMinY(), level.getHeight()));
                var heightmaps = new JsonObject();
                for (var type : Heightmap.Types.values()) {
                    var bytes = new ByteArrayOutputStream();
                    var values = new DataOutputStream(bytes);
                    for (int localZ = 0; localZ < 16; localZ++) {
                        for (int localX = 0; localX < 16; localX++) {
                            values.writeInt(chunk.getHeight(type, localX, localZ) + 1);
                        }
                    }
                    heightmaps.addProperty(type.getSerializationKey(), compressed(bytes.toByteArray()));
                }
                result.add("heightmaps", heightmaps);
                results.add(result);
            }
        }
        entry.add("chunks", results);
        var marks = new JsonArray();
        featureMarks(chunks, chunkX, chunkZ).forEach(position -> marks.add(coordinates(position)));
        entry.add("marks", marks);
        entry.add("blockEntities", blockEntities(region, chunks, chunkX, chunkZ));
        if (entry.has("verifyStatus") && entry.get("verifyStatus").getAsBoolean()) {
            var actual = level.getChunkSource().getChunk(chunkX, chunkZ, ChunkStatus.FEATURES, true);
            var actualBlocks = blocks(actual, level.getMinY(), level.getHeight());
            var isolatedBlocks = blocks(center, level.getMinY(), level.getHeight());
            if (!actualBlocks.get("hash").equals(isolatedBlocks.get("hash"))) {
                int differences = 0;
                var first = new ArrayList<String>();
                for (var pos : BlockPos.betweenClosed(chunkX * 16, level.getMinY(), chunkZ * 16,
                    chunkX * 16 + 15, level.getMaxY(), chunkZ * 16 + 15)) {
                    var expected = actual.getBlockState(pos);
                    var isolated = center.getBlockState(pos);
                    if (expected != isolated) {
                        differences++;
                        if (first.size() < 12) {
                            first.add(pos.toShortString() + ": " + expected + " vs " + isolated);
                        }
                    }
                }
                throw new IllegalStateException("Isolated decoration differs from FEATURES at "
                    + center.getPos() + ": " + differences + " differences: " + first);
            }
        }
    }

    // ---------------------------------------------------------------- structures

    // Obsidian's class for each structure piece type, by registry id: both sides' piece lines start with it.
    static final Map<String, String> PIECE_CLASSES = new HashMap<>();
    static {
        String[][] pieces = {
            {"jigsaw", "PoolElementStructurePiece"},
            {"mscorridor", "MineshaftCorridor"},
            {"mscrossing", "MineshaftCrossing"},
            {"msroom", "MineshaftRoom"},
            {"msstairs", "MineshaftStairs"},
            {"shcc", "ChestCorridor"},
            {"shfc", "FillerCorridor"},
            {"sh5c", "FiveCrossing"},
            {"shlt", "LeftTurn"},
            {"shli", "Library"},
            {"shpr", "PortalRoom"},
            {"shph", "PrisonHall"},
            {"shrt", "RightTurn"},
            {"shrc", "RoomCrossing"},
            {"shsd", "StairsDown"},
            {"shstart", "StartPiece"},
            {"shs", "Straight"},
            {"shssd", "StraightStairsDown"},
            {"nebcr", "BridgeCrossing"},
            {"nebef", "BridgeEndFiller"},
            {"nebs", "BridgeStraight"},
            {"neccs", "CastleCorridorStairsPiece"},
            {"nectb", "CastleCorridorTBalconyPiece"},
            {"nece", "CastleEntrance"},
            {"nescsc", "CastleSmallCorridorCrossingPiece"},
            {"nesclt", "CastleSmallCorridorLeftTurnPiece"},
            {"nesc", "CastleSmallCorridorPiece"},
            {"nescrt", "CastleSmallCorridorRightTurnPiece"},
            {"necsr", "CastleStalkRoom"},
            {"nemt", "MonsterThrone"},
            {"nerc", "RoomCrossing"},
            {"nesr", "StairsRoom"},
            {"nestart", "StartPiece"},
            {"tejp", "JungleTemplePiece"},
            {"tesh", "SwampHutPiece"},
            {"tedp", "DesertPyramidPiece"},
            {"omb", "MonumentBuilding"},
            {"omcr", "OceanMonumentCoreRoom"},
            {"omdxr", "OceanMonumentDoubleXRoom"},
            {"omdxyr", "OceanMonumentDoubleXYRoom"},
            {"omdyr", "OceanMonumentDoubleYRoom"},
            {"omdyzr", "OceanMonumentDoubleYZRoom"},
            {"omdzr", "OceanMonumentDoubleZRoom"},
            {"omentry", "OceanMonumentEntryRoom"},
            {"ompenthouse", "OceanMonumentPenthouse"},
            {"omsimple", "OceanMonumentSimpleRoom"},
            {"omsimplet", "OceanMonumentSimpleTopRoom"},
            {"omwr", "OceanMonumentWingRoom"},
            {"btp", "BuriedTreasurePiece"},
            {"orp", "OceanRuinPiece"},
            {"iglu", "IglooPiece"},
            {"ecp", "EndCityPiece"},
            {"wmp", "WoodlandMansionPiece"},
            {"shipwreck", "ShipwreckPiece"},
            {"nefos", "NetherFossilPiece"},
            {"rupo", "RuinedPortalPiece"},
        };
        for (var piece : pieces) {
            PIECE_CLASSES.put(piece[0], piece[1]);
        }
    }

    static void structures(JsonObject entry) {
        try {
            structuresChecked(entry);
        } catch (Exception exception) {
            throw new RuntimeException(exception);
        }
    }

    // A structure's start, found near the case's search origin unless the case names its chunk: its piece list,
    // then, for cases with "blocks", the blocks its pieces hold once structures alone are placed in each chunk.
    static void structuresChecked(JsonObject entry) throws Exception {
        System.out.println("Structure " + entry.get("name"));
        var level = level(entry.get("dimension").getAsString());
        var registry = server.registryAccess().lookupOrThrow(Registries.STRUCTURE);
        var key = ResourceKey.create(Registries.STRUCTURE, Identifier.parse(entry.get("structure").getAsString()));
        var holder = registry.getOrThrow(key);
        if (!entry.has("chunkX")) {
            var origin = new BlockPos(entry.get("searchX").getAsInt(), 64, entry.get("searchZ").getAsInt());
            var found = level.getChunkSource().getGenerator().findNearestMapStructure(
                level,
                net.minecraft.core.HolderSet.direct(holder),
                origin,
                entry.has("searchRadius") ? entry.get("searchRadius").getAsInt() : 100,
                false);
            if (found == null) {
                throw new IllegalStateException("No " + key + " near " + origin);
            }
            entry.addProperty("chunkX", found.getFirst().getX() >> 4);
            entry.addProperty("chunkZ", found.getFirst().getZ() >> 4);
        }

        int chunkX = entry.get("chunkX").getAsInt();
        int chunkZ = entry.get("chunkZ").getAsInt();
        var start = level.getChunk(chunkX, chunkZ, ChunkStatus.STRUCTURE_STARTS, true)
            .getStartForStructure(holder.value());
        if (start == null || !start.isValid()) {
            throw new IllegalStateException("No " + key + " starts in chunk " + chunkX + ", " + chunkZ);
        }

        var lines = new ArrayList<String>();
        for (var piece : start.getPieces()) {
            lines.add(describePiece(piece));
        }
        entry.addProperty("pieceCount", lines.size());
        entry.addProperty("pieces", hexHash(String.join("\n", lines).getBytes(StandardCharsets.UTF_8)));

        if (entry.has("blocks")) {
            entry.add("blocks", structureBlocks(level, start));
            entry.add("structureOrder", structureOrder(registry));
        }
    }

    // Every structure, by decoration step, in the registry order that seeds its placement.
    static JsonArray structureOrder(
        net.minecraft.core.Registry<net.minecraft.world.level.levelgen.structure.Structure> registry) {
        var steps = new JsonArray();
        for (var step : GenerationStep.Decoration.values()) {
            var ids = new JsonArray();
            registry.stream()
                .filter(structure -> structure.step() == step)
                .forEach(structure -> ids.add(registry.getKey(structure).toString()));
            steps.add(ids);
        }
        return steps;
    }

    // The states inside the start's piece boxes, chunk by chunk (x, then z) over the chunks its box reaches, then
    // piece by piece in y, z, x order. Each chunk is fresh terrain (with its beardifier) whose structures alone are
    // placed, step by step with decoration's seeds, as applyBiomeDecoration places them before features. The boxes
    // are the generated ones: template pieces move theirs to where they're placed.
    static JsonObject structureBlocks(
        ServerLevel level,
        net.minecraft.world.level.levelgen.structure.StructureStart start) throws Exception {
        var generator = (NoiseBasedChunkGenerator) level.getChunkSource().getGenerator();
        var randomState = level.getChunkSource().randomState();
        var source = generator.getBiomeSource();
        var settings = generator.generatorSettings().value();
        var registry = server.registryAccess().lookupOrThrow(Registries.STRUCTURE);
        var step = ChunkPyramid.GENERATION_PYRAMID.getStepTo(ChunkStatus.FEATURES);
        var manager = new BiomeManager(source.createUncachedResolver(randomState),
            BiomeManager.obfuscateSeed(level.getSeed()));
        var writableArea = ChunkGenerator.class.getDeclaredMethod("getWritableArea", ChunkAccess.class);
        writableArea.setAccessible(true);
        var box = start.getBoundingBox();
        var pieceBoxes = new ArrayList<net.minecraft.world.level.levelgen.structure.BoundingBox>();
        for (var piece : start.getPieces()) {
            var generated = piece.getBoundingBox();
            pieceBoxes.add(new net.minecraft.world.level.levelgen.structure.BoundingBox(
                generated.minX(),
                generated.minY(),
                generated.minZ(),
                generated.maxX(),
                generated.maxY(),
                generated.maxZ()));
        }
        var states = new LinkedHashMap<String, Integer>();
        var bytes = new ByteArrayOutputStream();
        var output = new DataOutputStream(bytes);
        int chunkCount = 0;

        for (int chunkX = box.minX() >> 4; chunkX <= box.maxX() >> 4; chunkX++) {
            for (int chunkZ = box.minZ() >> 4; chunkZ <= box.maxZ() >> 4; chunkZ++) {
                var chunks = new HashMap<Long, ProtoChunk>();
                // Each chunk keeps the world's starts and references, which placing reads through the region.
                StaticCache2D<ChunkAccess> cache = StaticCache2D.create(chunkX, chunkZ,
                    step.accumulatedDependencies().getRadius(), (x, z) -> {
                        var chunk = new ProtoChunk(new ChunkPos(x, z), UpgradeData.EMPTY, level,
                            PalettedContainerFactory.create(server.registryAccess()), null);
                        var world = level.getChunk(x, z, ChunkStatus.STRUCTURE_REFERENCES, true);
                        chunk.setAllStarts(world.getAllStarts());
                        chunk.setAllReferences(world.getAllReferences());
                        chunks.put(ChunkPos.pack(x, z), chunk);
                        return chunk;
                    });
                var center = chunks.get(ChunkPos.pack(chunkX, chunkZ));
                var region = region(level, cache, step, center);
                var structureManager = level.structureManager().forWorldGenRegion(region);

                for (int z = chunkZ - 1; z <= chunkZ + 1; z++) {
                    for (int x = chunkX - 1; x <= chunkX + 1; x++) {
                        var chunk = chunks.get(ChunkPos.pack(x, z));
                        generator.createBiomes(randomState, Blender.empty(), level.structureManager(), chunk).join();
                        var volume = new DensityVolume(16, settings.noiseSettings().height(), 16,
                            x * 16, settings.noiseSettings().minY(), z * 16);
                        var beardifier = Beardifier.forStructuresInChunk(level.structureManager(), new ChunkPos(x, z));
                        try (var noise = new NoiseChunk(randomState, beardifier, settings,
                            fluidPicker(settings), Blender.empty(), volume)) {
                            invokeStage(generator, "doFill", noise, chunk);
                            invokeStage(generator, "buildSurface", chunk, noise, randomState, manager,
                                source.possibleBiomes(), settings.materialRule().value());
                            invokeStage(generator, "generateCarvers", chunk, Blender.empty(), noise,
                                randomState, manager, null, settings.materialRule().value());
                        }
                        chunk.setPersistedStatus(ChunkStatus.TERRAIN);
                        Heightmap.primeHeightmaps(chunk, ChunkStatus.FEATURES.heightmapsAfter());
                    }
                }

                var centerPos = center.getPos();
                var area = (net.minecraft.world.level.levelgen.structure.BoundingBox) writableArea.invoke(null, center);
                var random = new WorldgenRandom(new XoroshiroRandomSource(0L));
                long decorationSeed = random.setDecorationSeed(level.getSeed(), centerPos.getMinBlockX(),
                    centerPos.getMinBlockZ());
                for (var decorationStep : GenerationStep.Decoration.values()) {
                    int index = 0;
                    var stepStructures = registry.stream()
                        .filter(value -> value.step() == decorationStep)
                        .toList();
                    for (var structure : stepStructures) {
                        random.setFeatureSeed(decorationSeed, index, decorationStep.ordinal());
                        for (var reaching : structureManager.startsForStructure(chunkX, chunkZ, structure)) {
                            reaching.placeInChunk(region, structureManager, generator, random, area, centerPos);
                        }
                        index++;
                    }
                }

                chunkCount++;
                for (var pieceBox : pieceBoxes) {
                    int minY = Math.max(pieceBox.minY(), level.getMinY());
                    int maxY = Math.min(pieceBox.maxY(), level.getMaxY());
                    int minX = Math.max(pieceBox.minX(), centerPos.getMinBlockX());
                    int maxX = Math.min(pieceBox.maxX(), centerPos.getMaxBlockX());
                    int minZ = Math.max(pieceBox.minZ(), centerPos.getMinBlockZ());
                    int maxZ = Math.min(pieceBox.maxZ(), centerPos.getMaxBlockZ());
                    for (int y = minY; y <= maxY; y++) {
                        for (int z = minZ; z <= maxZ; z++) {
                            for (int x = minX; x <= maxX; x++) {
                                var name = stateName(center.getBlockState(new BlockPos(x, y, z)));
                                output.writeInt(states.computeIfAbsent(name, value -> states.size()));
                            }
                        }
                    }
                }
            }
        }

        var result = new JsonObject();
        var palette = new JsonArray();
        states.keySet().forEach(palette::add);
        result.addProperty("chunks", chunkCount);
        result.add("palette", palette);
        result.addProperty("data", compressed(bytes.toByteArray()));
        var canonical = new ByteArrayOutputStream();
        canonical.write(palette.toString().getBytes(StandardCharsets.UTF_8));
        canonical.write(bytes.toByteArray());
        result.addProperty("hash", hexHash(canonical.toByteArray()));
        return result;
    }

    // One line per piece, as Obsidian's VanillaStructurePieces test describes its own pieces.
    static String describePiece(net.minecraft.world.level.levelgen.structure.StructurePiece piece) throws Exception {
        var type = BuiltInRegistries.STRUCTURE_PIECE.getKey(piece.getType()).getPath();
        var className = PIECE_CLASSES.get(type);
        if (className == null) {
            throw new IllegalStateException("Add Obsidian's class for structure piece type " + type);
        }

        var parts = new ArrayList<String>(List.of(className, "box " + box(piece.getBoundingBox())));
        if (piece instanceof net.minecraft.world.level.levelgen.structure.PoolElementStructurePiece pool) {
            parts.add(poolElement(pool.getElement()));
            parts.add(pool.getElement().getProjection().name().equals("RIGID") ? "rigid" : "terrain_matching");
            parts.add("pos " + blockPosition(pool.getPosition()));
            parts.add(pool.getRotation().name());
            parts.add("ground " + pool.getGroundLevelDelta());
            var junctions = new ArrayList<String>();
            for (var junction : pool.getJunctions()) {
                var rigid = junction.getDestProjection().name().equals("RIGID") ? 1 : 0;
                junctions.add(junction.getSourceX() + "," + junction.getSourceGroundY() + ","
                    + junction.getSourceZ() + "," + junction.getDeltaY() + "," + rigid);
            }
            parts.add("junctions " + String.join(";", junctions));
        } else if (piece instanceof net.minecraft.world.level.levelgen.structure.TemplateStructurePiece template) {
            var owner = net.minecraft.world.level.levelgen.structure.TemplateStructurePiece.class;
            parts.add((String) declaredField(owner, "templateName").get(template));
            parts.add("pos " + blockPosition(template.templatePosition()));
            var settings = template.placeSettings();
            parts.add(settings.getRotation().name());
            parts.add(settings.getMirror().name());
            parts.add("pivot " + blockPosition(settings.getRotationPivot()));
        } else {
            var orientation = piece.getOrientation();
            parts.add(orientation == null ? "none" : orientation.getSerializedName());
            parts.add("depth " + piece.getGenDepth());
        }

        return String.join(" | ", parts);
    }

    static String poolElement(net.minecraft.world.level.levelgen.structure.pools.StructurePoolElement element)
        throws Exception {
        var single = net.minecraft.world.level.levelgen.structure.pools.SinglePoolElement.class;
        if (single.isInstance(element)) {
            var template = (com.mojang.datafixers.util.Either<?, ?>) declaredField(single, "template").get(element);
            var location = template.left().orElseThrow();
            var legacy = element instanceof net.minecraft.world.level.levelgen.structure.pools.LegacySinglePoolElement;
            return (legacy ? "legacy:" : "single:") + location;
        }

        var list = net.minecraft.world.level.levelgen.structure.pools.ListPoolElement.class;
        if (list.isInstance(element)) {
            var parts = new ArrayList<String>();
            for (var child : (List<?>) declaredField(list, "elements").get(element)) {
                var childElement = (net.minecraft.world.level.levelgen.structure.pools.StructurePoolElement) child;
                parts.add(poolElement(childElement));
            }
            return "list:[" + String.join(",", parts) + "]";
        }

        var feature = net.minecraft.world.level.levelgen.structure.pools.FeaturePoolElement.class;
        if (feature.isInstance(element)) {
            return "feature:" + ((Holder<?>) declaredField(feature, "feature").get(element)).getRegisteredName();
        }

        return "empty";
    }

    static Field declaredField(Class<?> owner, String name) throws NoSuchFieldException {
        var field = owner.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    static String box(net.minecraft.world.level.levelgen.structure.BoundingBox box) {
        return box.minX() + "," + box.minY() + "," + box.minZ() + ","
            + box.maxX() + "," + box.maxY() + "," + box.maxZ();
    }

    static String blockPosition(BlockPos position) {
        return position.getX() + "," + position.getY() + "," + position.getZ();
    }

    // ---------------------------------------------------------------- features

    // One placed feature on prepared terrain, without biome selection: every chunk of a features-step region holds
    // the case's layers (each fills from the previous top + 1 up to its own top; air above) and the case's biome.
    // Vanilla's own WorldGenRegion, FeaturePlacer and decoration random place it the way applyBiomeDecoration does
    // for one feature, and the blocks of the writable 3x3 chunks that differ from the layers are recorded.
    static void feature(JsonObject entry) {
        try {
            featureChecked(entry);
        } catch (Exception exception) {
            throw new RuntimeException(exception);
        }
    }

    static void featureChecked(JsonObject entry) throws Exception {
        var dimension = entry.has("dimension")
            ? entry.get("dimension").getAsString()
            : "minecraft:overworld";
        var level = level(dimension);
        var generator = level.getChunkSource().getGenerator();

        if (entry.has("dimension")) {
            entry.addProperty("minY", level.getMinY());
            entry.addProperty("height", level.getHeight());
            entry.addProperty("seaLevel", generator.getSeaLevel());
        }

        long seed = entry.get("seed").getAsLong();
        int chunkX = entry.get("chunkX").getAsInt();
        int chunkZ = entry.get("chunkZ").getAsInt();
        var biomeKey = ResourceKey.create(Registries.BIOME, Identifier.parse(entry.get("biome").getAsString()));
        var biome = server.registryAccess().lookupOrThrow(Registries.BIOME).getOrThrow(biomeKey);
        var layers = layers(entry);
        var step = ChunkPyramid.GENERATION_PYRAMID.getStepTo(ChunkStatus.FEATURES);
        int radius = step.accumulatedDependencies().getRadius();

        var chunks = new HashMap<Long, ProtoChunk>();
        StaticCache2D<ChunkAccess> cache = StaticCache2D.create(chunkX, chunkZ, radius, (x, z) -> {
            var chunk = layeredChunk(level, new ChunkPos(x, z), layers, biome, entry);
            chunks.put(ChunkPos.pack(x, z), chunk);
            return chunk;
        });
        var center = chunks.get(ChunkPos.pack(chunkX, chunkZ));
        var region = region(level, cache, step, center);
        if (entry.has("regionRandomSeed")) {
            setField(region, "random", new XoroshiroRandomSource(entry.get("regionRandomSeed").getAsLong()));
        }

        var ops = RegistryOps.create(JsonOps.INSTANCE, server.registryAccess());
        var random = new WorldgenRandom(new XoroshiroRandomSource(0L));
        int minX = chunkX * 16;
        int minZ = chunkZ * 16;
        long decorationSeed = random.setDecorationSeed(seed, minX, minZ);
        random.setFeatureSeed(decorationSeed, entry.get("index").getAsInt(), entry.get("step").getAsInt());
        if (entry.has("randomSeed")) {
            random = new WorldgenRandom(new LegacyRandomSource(entry.get("randomSeed").getAsLong()));
        }
        var origin = entry.has("origin")
            ? position(entry.getAsJsonArray("origin"))
            : new BlockPos(minX, region.getMinY(), minZ);

        if (entry.has("probe")) {
            component(entry, region, generator, random, origin, ops);
            entry.addProperty("nextRandom", random.nextInt());
            return;
        }

        var placed = net.minecraft.world.level.levelgen.placement.PlacedFeature.CODEC
            .parse(ops, entry.get("feature")).getOrThrow().value();
        boolean result = new FeaturePlacer(region, generator).placeWithBiomeCheck(placed, random, origin);
        entry.addProperty("nextRandom", random.nextInt());

        entry.addProperty("regionRadius", radius);
        entry.addProperty("placed", result);
        entry.add("changes", changes(region, chunkX, chunkZ, layers, entry));
        if (entry.has("captureFluidTicks")) {
            entry.add("fluidTicks", featureFluidTicks(chunks, chunkX, chunkZ));
        }
        if (entry.has("regionRandomSeed")) {
            entry.addProperty("nextRegionRandom", region.getRandom().nextInt());
        }
        if (entry.has("recordBlockEntities")) {
            entry.add("blockEntities", blockEntities(region, chunks, chunkX, chunkZ));
        }
        if (entry.has("postProcess") || entry.has("captureMarks")) {
            var marks = featureMarks(chunks, chunkX, chunkZ);
            var recorded = new JsonArray();
            marks.forEach(pos -> recorded.add(coordinates(pos)));
            entry.add("marks", recorded);

            if (!entry.has("postProcess")) {
                return;
            }

            // These cases contain no fluids or block-specific post-processing; this is vanilla's shape pass.
            for (var pos : marks) {
                var state = region.getBlockState(pos);
                var updated = net.minecraft.world.level.block.Block.updateFromNeighbourShapes(state, region, pos);
                region.setBlock(pos, updated, 18);
            }
            entry.add("postChanges", changes(region, chunkX, chunkZ, layers, entry));
        }
    }

    static JsonArray featureFluidTicks(Map<Long, ProtoChunk> chunks, int centerX, int centerZ) {
        var result = new JsonArray();
        for (int z = centerZ - 1; z <= centerZ + 1; z++) {
            for (int x = centerX - 1; x <= centerX + 1; x++) {
                var chunk = chunks.get(ChunkPos.pack(x, z));
                for (var tick : chunk.getTicksForSerialization(0L).fluids()) {
                    var row = coordinates(tick.pos());
                    row.add(BuiltInRegistries.FLUID.getKey(tick.type()).toString());
                    row.add(tick.delay());
                    row.add(tick.priority().getValue());
                    result.add(row);
                }
            }
        }
        return result;
    }

    static List<BlockPos> featureMarks(Map<Long, ProtoChunk> chunks, int centerX, int centerZ) {
        var result = new ArrayList<BlockPos>();
        for (int z = centerZ - 1; z <= centerZ + 1; z++) {
            for (int x = centerX - 1; x <= centerX + 1; x++) {
                var chunk = chunks.get(ChunkPos.pack(x, z));
                var sections = chunk.getPostProcessing();
                for (int i = 0; i < sections.length; i++) {
                    if (sections[i] == null) {
                        continue;
                    }
                    for (short packed : sections[i]) {
                        result.add(ProtoChunk.unpackOffsetCoordinates(
                            packed, chunk.getSectionYFromSectionIndex(i), chunk.getPos()));
                    }
                }
            }
        }
        return result;
    }

    // The block entities of the writable chunks, as [x, y, z, type, data] by Y, Z and X. The data is what
    // vanilla saves for them without the id and position, in JSON. A block placed without reading its block entity
    // leaves only a pending DUMMY tag in a protochunk, so each is read through the region first, which creates it
    // the way the chunk's load would.
    static JsonArray blockEntities(WorldGenRegion region, Map<Long, ProtoChunk> chunks, int centerX, int centerZ) {
        var registries = server.registryAccess();
        var saved = new ArrayList<Map.Entry<BlockPos, net.minecraft.nbt.CompoundTag>>();
        for (int z = centerZ - 1; z <= centerZ + 1; z++) {
            for (int x = centerX - 1; x <= centerX + 1; x++) {
                var chunk = chunks.get(ChunkPos.pack(x, z));
                for (var pos : Set.copyOf(chunk.getBlockEntitiesPos())) {
                    region.getBlockEntity(pos);
                    var tag = chunk.getBlockEntityNbtForSaving(pos, registries);
                    saved.add(Map.entry(pos, tag));
                }
            }
        }
        Comparator<BlockPos> order = Comparator
            .comparingInt((BlockPos pos) -> pos.getY())
            .thenComparingInt(pos -> pos.getZ())
            .thenComparingInt(pos -> pos.getX());
        saved.sort(Map.Entry.comparingByKey(order));

        var result = new JsonArray();
        for (var blockEntity : saved) {
            var data = blockEntity.getValue().copy();
            var type = data.getStringOr("id", "");
            for (var metadata : List.of("id", "x", "y", "z", "keepPacked")) {
                data.remove(metadata);
            }
            if (data.getCompoundOrEmpty("components").isEmpty()) {
                data.remove("components");
            }
            var record = coordinates(blockEntity.getKey());
            record.add(type);
            record.add(net.minecraft.nbt.NbtOps.INSTANCE.convertTo(JsonOps.INSTANCE, data));
            result.add(record);
        }
        return result;
    }

    static BlockPos position(JsonArray coordinates) {
        int x = coordinates.get(0).getAsInt();
        int y = coordinates.get(1).getAsInt();
        int z = coordinates.get(2).getAsInt();
        return new BlockPos(x, y, z);
    }

    // Small component probes stay in the features section and use vanilla's own codecs and runtime methods.
    static void component(
        JsonObject entry,
        WorldGenRegion region,
        ChunkGenerator generator,
        net.minecraft.util.RandomSource random,
        BlockPos origin,
        RegistryOps<JsonElement> ops) {
        switch (entry.get("probe").getAsString()) {
            case "provider" -> {
                var provider = net.minecraft.world.level.levelgen.feature.stateproviders.BlockStateProvider.CODEC
                    .parse(ops, entry.get("provider")).getOrThrow().value();
                boolean required = entry.has("requiredState") && entry.get("requiredState").getAsBoolean();
                var state = required
                    ? provider.getState(region, random, origin)
                    : provider.getOptionalState(region, random, origin);
                entry.add("state", state == null ? JsonNull.INSTANCE : new JsonPrimitive(stateName(state)));
            }
            case "modifier" -> {
                var modifier = net.minecraft.world.level.levelgen.placement.PlacementModifier.CODEC
                    .parse(ops, entry.get("modifier")).getOrThrow();
                var context = new net.minecraft.world.level.levelgen.placement.PlacementContext(
                    region, generator, Optional.empty());
                var positions = new JsonArray();
                modifier.modify(context, random, origin, pos -> positions.add(coordinates(pos)));
                entry.add("positions", positions);
            }
            case "predicate" -> {
                var predicate = net.minecraft.world.level.levelgen.blockpredicates.BlockPredicate.CODEC
                    .parse(ops, entry.get("predicate")).getOrThrow();
                entry.addProperty("result", predicate.test(region, origin));
            }
            case "rule" -> {
                var rule = net.minecraft.world.level.levelgen.structure.templatesystem.RuleTest.CODEC
                    .parse(ops, entry.get("rule")).getOrThrow();
                var state = BlockState.CODEC.parse(ops, entry.get("inputState")).getOrThrow();
                entry.addProperty("result", rule.test(state, origin, random));
            }
            default -> throw new IllegalArgumentException("Unknown feature probe " + entry.get("probe"));
        }
    }

    static JsonArray coordinates(BlockPos position) {
        var result = new JsonArray();
        result.add(position.getX());
        result.add(position.getY());
        result.add(position.getZ());
        return result;
    }

    // The case's layers from the bottom up: a block id and the highest Y it fills.
    static List<Map.Entry<BlockState, Integer>> layers(JsonObject entry) {
        var result = new ArrayList<Map.Entry<BlockState, Integer>>();
        for (var element : entry.getAsJsonArray("layers")) {
            var layer = element.getAsJsonArray();
            var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(layer.get(0).getAsString()));
            result.add(Map.entry(block.defaultBlockState(), layer.get(1).getAsInt()));
        }
        return result;
    }

    static BlockState layerState(List<Map.Entry<BlockState, Integer>> layers, int y) {
        for (var layer : layers) {
            if (y <= layer.getValue()) {
                return layer.getKey();
            }
        }
        return net.minecraft.world.level.block.Blocks.AIR.defaultBlockState();
    }

    // A chunk at the terrain status, as decoration finds it: final heightmaps follow writes from here on, while the
    // world generation heightmaps keep the terrain's values.
    static ProtoChunk layeredChunk(
        ServerLevel level,
        ChunkPos position,
        List<Map.Entry<BlockState, Integer>> layers,
        Holder<Biome> biome,
        JsonObject entry) {
        var chunk = new ProtoChunk(
            position,
            UpgradeData.EMPTY,
            level,
            PalettedContainerFactory.create(server.registryAccess()),
            null);
        var block = new BlockPos.MutableBlockPos();
        int minY = chunk.getMinY();
        int topY = layers.getLast().getValue();
        for (int y = minY; y <= topY; y++) {
            var state = layerState(layers, y);
            if (state.isAir()) {
                continue;
            }
            for (int z = 0; z < 16; z++) {
                for (int x = 0; x < 16; x++) {
                    chunk.setBlockState(block.set(position.getMinBlockX() + x, y, position.getMinBlockZ() + z), state);
                }
            }
        }
        if (entry.has("edits")) {
            var ops = RegistryOps.create(JsonOps.INSTANCE, server.registryAccess());
            for (var element : entry.getAsJsonArray("edits")) {
                var edit = element.getAsJsonArray();
                var pos = position(edit);
                if ((pos.getX() >> 4) == position.x() && (pos.getZ() >> 4) == position.z()) {
                    var state = BlockState.CODEC.parse(ops, edit.get(3)).getOrThrow();
                    chunk.setBlockState(pos, state);
                    if (entry.has("recordBlockEntities")
                        && state.getBlock() instanceof net.minecraft.world.level.block.EntityBlock entityBlock) {
                        var entity = entityBlock.newBlockEntity(pos, state);
                        if (entity != null) {
                            chunk.setBlockEntity(entity);
                        }
                    }
                }
            }
        }
        chunk.fillBiomesFromNoise((x, y, z) -> biome);
        chunk.setPersistedStatus(ChunkStatus.TERRAIN);
        Heightmap.primeHeightmaps(chunk, EnumSet.allOf(Heightmap.Types.class));
        return chunk;
    }

    // Vanilla's WorldGenRegion maps generation holders to chunks in its constructor. These chunks have no holders,
    // so the region is allocated without it and given the fields the constructor would set.
    static WorldGenRegion region(
        ServerLevel level,
        StaticCache2D<ChunkAccess> cache,
        ChunkStep step,
        ChunkAccess center) throws Exception {
        var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
        var region = (WorldGenRegion) allocate.invoke(unsafe, WorldGenRegion.class);

        var randomName = WorldGenRegion.class.getDeclaredField("WORLDGEN_REGION_RANDOM");
        randomName.setAccessible(true);
        var randomFactory = level.getChunkSource()
            .randomState()
            .getOrCreateRandomFactory((Identifier) randomName.get(null));
        var seed = level.getSeed();

        setField(region, "cache", cache);
        setField(region, "center", center);
        setField(region, "level", level);
        setField(region, "seed", seed);
        setField(region, "levelData", level.getLevelData());
        setField(region, "random", randomFactory.at(center.getPos().getWorldPosition()));
        setField(region, "dimensionType", level.dimensionType());
        setField(region, "uncachedBiomeResolver", level.uncachedBiomeResolver());
        setField(region, "blockTicks", new WorldGenTickAccess<>(pos -> region.getChunk(pos).getBlockTicks()));
        setField(region, "fluidTicks", new WorldGenTickAccess<>(pos -> region.getChunk(pos).getFluidTicks()));
        setField(region, "subTickCount", new java.util.concurrent.atomic.AtomicLong());
        setField(region, "generatingStep", step);
        setField(region, "biomeManager", new BiomeManager(region, BiomeManager.obfuscateSeed(seed)));
        setField(region, "centerChunkX", center.getPos().x());
        setField(region, "centerChunkZ", center.getPos().z());
        setField(region, "writeRadius", step.blockStateWriteRadius());
        Supplier<String> generating = () -> "feature capture";
        setField(region, "currentlyGenerating", generating);

        var attributes = EnvironmentAttributeSystem.builder().addStaticLayers(region).build();
        setField(region, "environmentAttributes", attributes);
        return region;
    }

    static void setField(Object target, String name, Object value) throws Exception {
        var field = target.getClass().getDeclaredField(name);
        field.setAccessible(true);
        field.set(target, value);
    }

    // The blocks of the writable chunks that differ from the layers, as [x, y, z, state] in z, x, y order.
    static JsonArray changes(
        WorldGenRegion region,
        int chunkX,
        int chunkZ,
        List<Map.Entry<BlockState, Integer>> layers,
        JsonObject entry) {
        var baseline = new HashMap<BlockPos, BlockState>();
        if (entry.has("edits")) {
            var ops = RegistryOps.create(JsonOps.INSTANCE, server.registryAccess());
            for (var element : entry.getAsJsonArray("edits")) {
                var edit = element.getAsJsonArray();
                baseline.put(position(edit), BlockState.CODEC.parse(ops, edit.get(3)).getOrThrow());
            }
        }
        var result = new JsonArray();
        var block = new BlockPos.MutableBlockPos();
        int minY = region.getMinY();
        int maxY = minY + region.getHeight();
        for (int z = chunkZ * 16 - 16; z < chunkZ * 16 + 32; z++) {
            for (int x = chunkX * 16 - 16; x < chunkX * 16 + 32; x++) {
                for (int y = minY; y < maxY; y++) {
                    var state = region.getBlockState(block.set(x, y, z));
                    var initial = baseline.getOrDefault(block, layerState(layers, y));
                    if (state == initial) {
                        continue;
                    }
                    var change = new JsonArray();
                    change.add(x);
                    change.add(y);
                    change.add(z);
                    change.add(stateName(state));
                    result.add(change);
                }
            }
        }
        return result;
    }
}
