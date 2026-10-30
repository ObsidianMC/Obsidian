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
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.biome.BiomeManager;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.chunk.*;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.levelgen.*;
import net.minecraft.world.level.levelgen.blending.Blender;
import net.minecraft.world.level.levelgen.densityfunction.DensityFunction;
import net.minecraft.world.level.levelgen.densityfunction.DensityVolume;
import net.minecraft.world.level.levelgen.material.MaterialRuleContext;
import net.minecraft.world.level.levelgen.material.rule.MaterialRule;
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
 * <p>Phase 5 of docs/porting-26.3-plan.md adds sections step by step. Terrain and material captures are here;
 * features, structures, lighting and spawn references follow in the later steps.
 */
class WorldgenCaptures {
    // Which world (structures on or off) each section's cases are captured in.
    static final Map<String, Boolean> SECTIONS = new LinkedHashMap<>();
    static {
        SECTIONS.put("noises", false);
        SECTIONS.put("router", false);
        SECTIONS.put("functions", false);
        SECTIONS.put("terrain", false);
        SECTIONS.put("materials", false);
        SECTIONS.put("aquifers", false);
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
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().serializeNulls().create();
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
                changes.add(property.getKey() + " " + brief(old) + " -> " + brief(property.getValue()));
            }
        }

        var outcome = changes.isEmpty() ? ": unchanged" : ": CHANGED " + String.join(", ", changes);
        var inputs = before.deepCopy();
        if (section.equals("terrain")) {
            inputs = new JsonObject();
            for (var field : List.of("seed", "dimension", "chunkX", "chunkZ")) {
                inputs.add(field, before.get(field));
            }
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
        var dimension = entry.get("dimension").getAsString();
        var levelKey = switch (dimension) {
            case "minecraft:overworld" -> Level.OVERWORLD;
            case "minecraft:the_nether" -> Level.NETHER;
            case "minecraft:the_end" -> Level.END;
            default -> throw new IllegalArgumentException(dimension);
        };
        var level = server.getLevel(levelKey);
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
            var generation = new WorldGenerationContext(generator, chunk.getHeightAccessorForGeneration());
            var mask = new CarvingMask(generation.getMinGenY() + 1,
                generation.getMinGenY() + generation.getGenDepth() - 8);
            var carvingRandom = new WorldgenRandom(new LegacyRandomSource(0L));
            for (int dx = -8; dx <= 8; dx++) {
                for (int dz = -8; dz <= 8; dz++) {
                    var source = new ChunkPos(chunkX + dx, chunkZ + dz);
                    var biome = resolver.getNoiseBiome(source.getMinBlockX() >> 2, 0, source.getMinBlockZ() >> 2);
                    int index = 0;
                    for (var carver : biome.value().getGenerationSettings().getCarvers()) {
                        carvingRandom.setLargeFeatureSeed(entry.get("seed").getAsLong() + index,
                            source.x(), source.z());
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
                    maskValues[volume.indexUnchecked(x, y - minY, z)] = 1;
                }
            });
            var maskBytes = new ByteArrayOutputStream();
            var maskOutput = new DataOutputStream(maskBytes);
            for (int value : maskValues) {
                maskOutput.writeInt(value);
            }
            entry.addProperty("carvingMask", compressed(maskBytes.toByteArray()));

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
        entry.add("carverSources", carvers);
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
}
