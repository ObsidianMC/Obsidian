import com.google.gson.*;
import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.security.MessageDigest;
import java.util.*;
import java.util.function.*;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.tags.TagKey;
import net.minecraft.world.item.MapItem;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.LightLayer;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.chunk.ChunkAccess;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.levelgen.DensityFunction;
import net.minecraft.world.level.levelgen.NoiseGeneratorSettings;
import net.minecraft.world.level.levelgen.RandomState;
import net.minecraft.world.level.levelgen.structure.BoundingBox;
import net.minecraft.world.level.levelgen.structure.PoolElementStructurePiece;
import net.minecraft.world.level.levelgen.structure.StructurePiece;
import net.minecraft.world.level.levelgen.structure.StructureStart;
import net.minecraft.world.level.levelgen.structure.TemplateStructurePiece;
import net.minecraft.world.level.levelgen.structure.pools.FeaturePoolElement;
import net.minecraft.world.level.levelgen.structure.pools.JigsawJunction;
import net.minecraft.world.level.levelgen.structure.pools.LegacySinglePoolElement;
import net.minecraft.world.level.levelgen.structure.pools.ListPoolElement;
import net.minecraft.world.level.levelgen.structure.pools.SinglePoolElement;
import net.minecraft.world.level.levelgen.structure.pools.StructurePoolElement;

/**
 * Re-records the vanilla worldgen and structure references of Obsidian's parity tests
 * (Obsidian.Tests/Assets/vanilla_worldgen.json); WorldgenCaptures.cs runs it (docs/vanilla-captures.md). Run it in an
 * empty directory with the server's class path and
 * {@code --add-opens java.base/java.lang=ALL-UNNAMED}, twice per world. First
 * {@code <fixture json> <seed> <generate structures> <version> create} creates and saves the world (picking its spawn
 * generates chunks around it), then the caller deletes its chunks ({@code region}, {@code entities} and {@code poi}
 * folders), then {@code <fixture json> <seed> <generate structures> <version>} captures. Worlds without cases exit right
 * away.
 *
 * <p>It runs a headless vanilla dedicated server in this JVM and has the server's own chunk map generate every chunk: a
 * case asks for chunks at the status it captures (SURFACE, CARVERS, FEATURES, LIGHT), in the order the case describes,
 * on the server thread, so no tick runs (and no chunk unloads) in between, and fails if a chunk was generated further
 * before. Full chunks of a server world are force-loaded instead, with ticks frozen. Only the fixture's cases for this
 * world are recaptured, and the fixture is rewritten in place.
 */
class WorldgenCaptures {
    // Which world (structures on or off) each section's cases are captured in.
    static final Map<String, Boolean> SECTIONS = new LinkedHashMap<>();
    static {
        // Lit chunks come before full chunks, which decorate their neighbors in the server's order.
        for (var name : List.of("router", "biomes", "chunks", "carvedChunks", "decoratedChunks", "litChunks", "fullChunks", "spawns"))
            SECTIONS.put(name, false);
        for (var name : List.of("structurePieces", "structureBlocks", "treasureMaps"))
            SECTIONS.put(name, true);
    }

    // Obsidian's class for each vanilla structure piece type (by registry id), which the piece list hashes start with.
    static final Map<String, String> PIECE_CLASSES = new HashMap<>();
    static {
        String[][] pieces = {
            {"jigsaw", "PoolElementStructurePiece"},
            {"mscorridor", "MineshaftCorridor"}, {"mscrossing", "MineshaftCrossing"}, {"msroom", "MineshaftRoom"},
            {"msstairs", "MineshaftStairs"},
            {"shcc", "ChestCorridor"}, {"shfc", "FillerCorridor"}, {"sh5c", "FiveCrossing"}, {"shlt", "LeftTurn"},
            {"shli", "Library"}, {"shpr", "PortalRoom"}, {"shph", "PrisonHall"}, {"shrt", "RightTurn"},
            {"shrc", "RoomCrossing"}, {"shsd", "StairsDown"}, {"shstart", "StartPiece"}, {"shs", "Straight"},
            {"shssd", "StraightStairsDown"},
            {"nebcr", "BridgeCrossing"}, {"nebef", "BridgeEndFiller"}, {"nebs", "BridgeStraight"},
            {"neccs", "CastleCorridorStairsPiece"}, {"nectb", "CastleCorridorTBalconyPiece"}, {"nece", "CastleEntrance"},
            {"nescsc", "CastleSmallCorridorCrossingPiece"}, {"nesclt", "CastleSmallCorridorLeftTurnPiece"},
            {"nesc", "CastleSmallCorridorPiece"}, {"nescrt", "CastleSmallCorridorRightTurnPiece"},
            {"necsr", "CastleStalkRoom"}, {"nemt", "MonsterThrone"}, {"nerc", "RoomCrossing"}, {"nesr", "StairsRoom"},
            {"nestart", "StartPiece"},
            {"ecp", "EndCityPiece"}, {"wmp", "WoodlandMansionPiece"},
        };
        for (var piece : pieces)
            PIECE_CLASSES.put(piece[0], piece[1]);
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

        var cases = new LinkedHashMap<String, List<JsonObject>>();
        for (var section : SECTIONS.entrySet()) {
            if (section.getValue() != structures || !fixture.has(section.getKey()))
                continue;
            for (var element : fixture.getAsJsonArray(section.getKey())) {
                var entry = element.getAsJsonObject();
                if (entry.get("seed").getAsLong() == seed)
                    cases.computeIfAbsent(section.getKey(), name -> new ArrayList<>()).add(entry);
            }
        }
        if (cases.isEmpty()) {
            System.out.println("No cases for seed " + seed + " with structures " + structures + ".");
            return;
        }

        if (create) {
            // Creates the world, which picks its spawn (generating the chunks around it), and saves it. The caller then
            // deletes the world's chunks, so that captures start without any.
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

        var report = new ArrayList<String>();
        try {
            startServer(seed, structures);
            // Like the /tick freeze the reference worlds were loaded under: force-loaded chunks tick no blocks or fluids.
            onServer(() -> {
                server.tickRateManager().setFrozen(true);
                return null;
            });
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

    // One line per case: the case's inputs, then whether its captured values are what the fixture held.
    static String compare(String section, JsonObject before, JsonObject after) {
        var changes = new ArrayList<String>();
        for (var property : after.entrySet()) {
            var old = before.get(property.getKey());
            if (old == null || !sameValue(old, property.getValue()))
                changes.add(property.getKey() + " " + old + " -> " + property.getValue());
        }
        return "  " + section + " " + before + (changes.isEmpty() ? ": unchanged" : ": CHANGED " + String.join(", ", changes));
    }

    // Numbers compare by value, since the fixture may spell them differently (64 or 64.0).
    static boolean sameValue(JsonElement a, JsonElement b) {
        if (a.isJsonPrimitive() && b.isJsonPrimitive() && a.getAsJsonPrimitive().isNumber() && b.getAsJsonPrimitive().isNumber())
            return new java.math.BigDecimal(a.getAsString()).compareTo(new java.math.BigDecimal(b.getAsString())) == 0;
        return a.equals(b);
    }

    // A capture runs on the server thread, so no tick (and no chunk unload) happens in the middle of it, except while it
    // waits for the server to load the chunks of a server world.
    static void capture(String section, JsonObject entry) {
        try {
            switch (section) {
                case "fullChunks" -> fullChunk(entry);
                case "structureBlocks" -> structureBlocks(entry);
                default -> onServer(() -> {
                    try {
                        switch (section) {
                            case "router" -> router(entry);
                            case "biomes" -> biome(entry);
                            case "chunks" -> surfaceChunk(entry);
                            case "carvedChunks" -> carvedChunk(entry);
                            case "decoratedChunks" -> decoratedChunk(entry);
                            case "spawns" -> spawn(entry);
                            case "litChunks" -> litChunk(entry);
                            case "structurePieces" -> structurePieces(entry);
                            case "treasureMaps" -> treasureMap(entry);
                            default -> throw new IllegalArgumentException(section);
                        }
                    } catch (ReflectiveOperationException e) {
                        throw new IllegalStateException(e);
                    }
                    return null;
                });
            }
        } catch (Exception e) {
            throw new RuntimeException("Capturing " + section + " " + entry + " failed", e);
        }
    }

    // ---------------------------------------------------------------- server

    static void startServer(long seed, boolean structures) throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");
        // No watchdog, since a capture runs on the server thread for as long as its chunks take, and no pausing while
        // nobody is online, since force-loaded chunks load as the server ticks.
        Files.writeString(Path.of("server.properties"), String.join("\n",
            "level-seed=" + seed, "generate-structures=" + structures, "online-mode=false", "server-port=0",
            "max-tick-time=-1", "pause-when-empty-seconds=-1", "spawn-protection=0", "sync-chunk-writes=false",
            "view-distance=2", "simulation-distance=2","enable-jmx-monitoring=false", "enable-rcon=false", "enable-query=false", ""));

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
        if (server == null)
            throw new IllegalStateException("The dedicated server didn't start.");

        while (!server.isReady())
            Thread.sleep(50);
    }

    static <T> T onServer(Supplier<T> work) {
        return server.submit(work).join();
    }

    /**
     * Full chunks of a server world: force-loaded, as /forceload does, and read once the server ticks them. A chunk is
     * post-processed (its shape updates, such as fence connections) only when it starts ticking, after its neighbors are
     * full.
     */
    static Map<Long, ChunkAccess> serverWorldChunks(ServerLevel level, List<int[]> positions) throws InterruptedException {
        onServer(() -> {
            for (var position : positions)
                level.setChunkForced(position[0], position[1], true);
            return null;
        });
        while (!onServer(() -> positions.stream().allMatch(position ->
                level.isPositionTickingWithEntitiesLoaded(ChunkPos.asLong(position[0], position[1])))))
            Thread.sleep(50);
        return onServer(() -> {
            var chunks = new HashMap<Long, ChunkAccess>();
            for (var position : positions)
                chunks.put(ChunkPos.asLong(position[0], position[1]), chunk(level, position[0], position[1], ChunkStatus.FULL));
            return chunks;
        });
    }

    static ServerLevel level(JsonObject entry) {
        var name = entry.has("dimension") ? entry.get("dimension").getAsString() : "overworld";
        var key = switch (name) {
            case "overworld" -> Level.OVERWORLD;
            case "nether" -> Level.NETHER;
            case "end" -> Level.END;
            default -> throw new IllegalArgumentException(name);
        };
        return server.getLevel(key);
    }

    static ChunkAccess chunk(ServerLevel level, int chunkX, int chunkZ, ChunkStatus status) {
        return level.getChunkSource().getChunk(chunkX, chunkZ, status, true);
    }

    /** A chunk generated to exactly a status, failing if something (such as spawn selection) took it further already. */
    static ChunkAccess chunkAt(ServerLevel level, int chunkX, int chunkZ, ChunkStatus status) {
        var chunk = chunk(level, chunkX, chunkZ, status);
        var actual = chunk.getPersistedStatus();
        if (actual != status) {
            throw new IllegalStateException("Chunk " + chunkX + ", " + chunkZ + " is at " + actual.getName() +
                " instead of " + status.getName() + "; the server generated it before the capture.");
        }
        return chunk;
    }

    // ---------------------------------------------------------------- captures

    // Vanilla: RandomState.create(overworld, seed).router().<field>().compute(new SinglePointContext(x, y, z)).
    static void router(JsonObject entry) {
        var random = RandomState.create(server.registryAccess(),
            NoiseGeneratorSettings.OVERWORLD, entry.get("seed").getAsLong());
        var router = random.router();
        var point = new DensityFunction.SinglePointContext(entry.get("x").getAsInt(), entry.get("y").getAsInt(), entry.get("z").getAsInt());
        entry.addProperty("continents", router.continents().compute(point));
        entry.addProperty("erosion", router.erosion().compute(point));
        entry.addProperty("depth", router.depth().compute(point));
        entry.addProperty("finalDensity", router.finalDensity().compute(point));
        entry.addProperty("preliminarySurfaceLevel", router.preliminarySurfaceLevel().compute(point));
    }

    // Vanilla: the overworld's multi-noise biome source, getNoiseBiome(quartX, quartY, quartZ, sampler).
    static void biome(JsonObject entry) {
        var random = RandomState.create(server.registryAccess(),
            NoiseGeneratorSettings.OVERWORLD, entry.get("seed").getAsLong());
        var source = level(entry).getChunkSource().getGenerator().getBiomeSource();
        var biome = source.getNoiseBiome(entry.get("quartX").getAsInt(), entry.get("quartY").getAsInt(),
            entry.get("quartZ").getAsInt(), random.sampler());
        entry.addProperty("biome", biome.getRegisteredName());
    }

    // The chunk after the biomes, noise and surface steps: its block names and quart biomes.
    static void surfaceChunk(JsonObject entry) {
        var chunk = chunkAt(level(entry), entry.get("chunkX").getAsInt(), entry.get("chunkZ").getAsInt(), ChunkStatus.SURFACE);
        entry.addProperty("blocks", sha256(blockNames(chunk)));
        entry.addProperty("biomes", sha256(biomeNames(chunk)));
    }

    // The chunk after the carvers step.
    static void carvedChunk(JsonObject entry) {
        var chunk = chunkAt(level(entry), entry.get("chunkX").getAsInt(), entry.get("chunkZ").getAsInt(), ChunkStatus.CARVERS);
        entry.addProperty("blocks", sha256(blockNames(chunk)));
    }

    // The chunk decorated with its neighbors at the carvers step (only the center is decorated).
    static void decoratedChunk(JsonObject entry) {
        var level = level(entry);
        int chunkX = entry.get("chunkX").getAsInt(), chunkZ = entry.get("chunkZ").getAsInt();
        for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
                chunkAt(level, chunkX + dx, chunkZ + dz, ChunkStatus.CARVERS);
        var chunk = chunkAt(level, chunkX, chunkZ, ChunkStatus.FEATURES);
        entry.addProperty("blocks", sha256(blockNames(chunk)));
    }

    // A full chunk of a server world.
    static void fullChunk(JsonObject entry) throws InterruptedException {
        int chunkX = entry.get("chunkX").getAsInt(), chunkZ = entry.get("chunkZ").getAsInt();
        var chunk = serverWorldChunks(level(entry), List.of(new int[]{chunkX, chunkZ})).get(ChunkPos.asLong(chunkX, chunkZ));
        entry.addProperty("blocks", onServer(() -> sha256(blockNames(chunk))));
    }

    // The spawn the server picked for the new world (level.dat's spawn).
    static void spawn(JsonObject entry) {
        var pos = server.getRespawnData().pos();
        entry.addProperty("x", pos.getX());
        entry.addProperty("y", pos.getY());
        entry.addProperty("z", pos.getZ());
    }

    // The chunk's sky light, then block light, per section bottom up in vanilla's nibble layout, once the 7x7 chunks around
    // it are carved, the 5x5 decorated (z then x ascending) and the 3x3 lit (z then x ascending).
    static void litChunk(JsonObject entry) {
        var level = level(entry);
        int chunkX = entry.get("chunkX").getAsInt(), chunkZ = entry.get("chunkZ").getAsInt();
        for (var dz = -3; dz <= 3; dz++)
            for (var dx = -3; dx <= 3; dx++)
                chunkAt(level, chunkX + dx, chunkZ + dz, ChunkStatus.CARVERS);
        for (var dz = -2; dz <= 2; dz++)
            for (var dx = -2; dx <= 2; dx++)
                chunkAt(level, chunkX + dx, chunkZ + dz, ChunkStatus.FEATURES);
        for (var dz = -1; dz <= 1; dz++)
            for (var dx = -1; dx <= 1; dx++)
                chunk(level, chunkX + dx, chunkZ + dz, ChunkStatus.LIGHT);

        var light = new ByteArrayOutputStream();
        var lightEngine = level.getLightEngine();
        for (var layer : List.of(LightLayer.SKY, LightLayer.BLOCK)) {
            var listener = lightEngine.getLayerListener(layer);
            int minSection = level.getMinSectionY(), sections = level.getSectionsCount();
            for (var section = minSection; section < minSection + sections; section++) {
                var nibbles = new byte[2048];
                for (var index = 0; index < 4096; index++) {
                    int x = index & 15, z = (index >> 4) & 15, y = index >> 8;
                    var value = listener.getLightValue(
                        new BlockPos((chunkX << 4) + x, (section << 4) + y, (chunkZ << 4) + z));
                    nibbles[index >> 1] |= (byte) (value << ((index & 1) << 2));
                }
                light.writeBytes(nibbles);
            }
        }
        entry.addProperty("light", sha256(light.toByteArray()));
    }

    // Vanilla: Structure.generate's piece list for the start in the chunk (the chunk's structure starts).
    static void structurePieces(JsonObject entry) throws ReflectiveOperationException {
        var start = start(entry);
        var lines = new ArrayList<String>();
        for (var piece : start.getPieces())
            lines.add(describe((StructurePiece) piece));
        entry.addProperty("pieceCount", lines.size());
        entry.addProperty("pieces", sha256(String.join("\n", lines)));
    }

    // The block state ids (little-endian uint16) inside each piece box, in piece order and y, z, x order, once every chunk
    // the structure reaches is full.
    static void structureBlocks(JsonObject entry) throws InterruptedException {
        var level = level(entry);
        var start = onServer(() -> start(entry));
        var box = start.getBoundingBox();
        var positions = new ArrayList<int[]>();
        for (var x = box.minX() >> 4; x <= box.maxX() >> 4; x++)
            for (var z = box.minZ() >> 4; z <= box.maxZ() >> 4; z++)
                positions.add(new int[]{x, z});
        var chunks = serverWorldChunks(level, positions);
        onServer(() -> {
            hashStructureBlocks(entry, level, start, chunks);
            return null;
        });
    }

    static void hashStructureBlocks(JsonObject entry, ServerLevel level, StructureStart start, Map<Long, ChunkAccess> chunks) {

        int minY = level.getMinY(), maxY = minY + level.getHeight() - 1;
        var ids = new ByteArrayOutputStream();
        for (var element : start.getPieces()) {
            var pieceBox = ((StructurePiece) element).getBoundingBox();
            for (var y = Math.max(pieceBox.minY(), minY); y <= Math.min(pieceBox.maxY(), maxY); y++) {
                for (var z = pieceBox.minZ(); z <= pieceBox.maxZ(); z++) {
                    for (var x = pieceBox.minX(); x <= pieceBox.maxX(); x++) {
                        var chunk = chunks.get(ChunkPos.asLong(x >> 4, z >> 4));
                        var id = Block.getId(chunk.getBlockState(new BlockPos(x, y, z)));
                        ids.write(id);
                        ids.write(id >> 8);
                    }
                }
            }
        }
        entry.addProperty("blocks", sha256(ids.toByteArray()));
    }

    // Vanilla: /loot spawn ... loot minecraft:chests/shipwreck_map. Its exploration_map function finds the structure, then
    // creates the map at it and renders its biome preview.
    static void treasureMap(JsonObject entry) {
        var level = level(entry);
        var tag = TagKey.create(Registries.STRUCTURE, Identifier.parse(entry.get("tag").getAsString()));
        var origin = new BlockPos(entry.get("x").getAsInt(), entry.get("y").getAsInt(), entry.get("z").getAsInt());
        var target = level.findNearestMapStructure(tag, origin, entry.get("radius").getAsInt(), entry.get("skipKnown").getAsBoolean());
        entry.addProperty("targetX", target.getX());
        entry.addProperty("targetY", target.getY());
        entry.addProperty("targetZ", target.getZ());

        var map = MapItem.create(level, target.getX(), target.getZ(), entry.get("zoom").getAsByte(), true, true);
        MapItem.renderBiomePreviewMap(level, map);
        var data = MapItem.getSavedData(map, level);
        entry.addProperty("centerX", data.centerX);
        entry.addProperty("centerZ", data.centerZ);
        entry.addProperty("colors", sha256(data.colors));
    }

    // ---------------------------------------------------------------- structures

    static StructureStart start(JsonObject entry) {
        var structure = entry.get("structure").getAsString();
        var chunk = chunk(level(entry), entry.get("chunkX").getAsInt(), entry.get("chunkZ").getAsInt(), ChunkStatus.STRUCTURE_STARTS);
        Registry structures = server.registryAccess().lookupOrThrow(Registries.STRUCTURE);
        for (var start : ((Map<?, ?>) chunk.getAllStarts()).entrySet()) {
            if (structures.getKey(start.getKey()).toString().equals(structure))
                return (StructureStart) start.getValue();
        }
        throw new IllegalStateException("No " + structure + " starts in chunk " + entry.get("chunkX") + ", " + entry.get("chunkZ"));
    }

    // One line per piece, as Obsidian's VanillaStructureGeneration test describes its own pieces.
    static String describe(StructurePiece piece) throws ReflectiveOperationException {
        var type = ((Identifier) BuiltInRegistries.STRUCTURE_PIECE.getKey(piece.getType())).getPath();
        var className = PIECE_CLASSES.get(type);
        if (className == null)
            throw new IllegalStateException("Add Obsidian's class for structure piece type " + type + " to PIECE_CLASSES.");

        var parts = new ArrayList<String>(List.of(className, "box " + box(piece.getBoundingBox())));
        if (piece instanceof PoolElementStructurePiece pool) {
            parts.add(element(pool.getElement()));
            parts.add(rigid(pool.getElement().getProjection()) ? "rigid" : "terrain_matching");
            parts.add("pos " + position(pool.getPosition()));
            parts.add(pool.getRotation().name());
            parts.add("ground " + pool.getGroundLevelDelta());
            var junctions = new ArrayList<String>();
            for (var element : pool.getJunctions()) {
                var junction = (JigsawJunction) element;
                junctions.add(junction.getSourceX() + "," + junction.getSourceGroundY() + "," +
                    junction.getSourceZ() + "," + junction.getDeltaY() + "," +
                    (rigid(junction.getDestProjection()) ? 1 : 0));
            }
            parts.add("junctions " + String.join(";", junctions));
        } else if (piece instanceof TemplateStructurePiece template) {
            parts.add((String) field(TemplateStructurePiece.class, "templateName").get(template));
            parts.add("pos " + position(template.templatePosition()));
            var settings = template.placeSettings();
            parts.add(settings.getRotation().name());
            parts.add(settings.getMirror().name());
            parts.add("pivot " + position(settings.getRotationPivot()));
        } else {
            var orientation = piece.getOrientation();
            parts.add(orientation == null ? "none" : orientation.getSerializedName());
            parts.add("depth " + piece.getGenDepth());
        }
        return String.join(" | ", parts);
    }

    static String element(StructurePoolElement element) throws ReflectiveOperationException {
        if (element instanceof LegacySinglePoolElement || element instanceof SinglePoolElement) {
            var template = (com.mojang.datafixers.util.Either<?, ?>) field(SinglePoolElement.class, "template").get(element);
            var location = template.left().orElseThrow(() -> new IllegalStateException("Pool element without a template id: " + element));
            return (element instanceof LegacySinglePoolElement ? "legacy:" : "single:") + location;
        }
        if (element instanceof ListPoolElement) {
            var parts = new ArrayList<String>();
            for (var child : (List<?>) field(ListPoolElement.class, "elements").get(element))
                parts.add(element((StructurePoolElement) child));
            return "list:[" + String.join(",", parts) + "]";
        }
        if (element instanceof FeaturePoolElement)
            return "feature:" + ((Holder) field(FeaturePoolElement.class, "feature").get(element)).getRegisteredName();
        return "empty";
    }

    // By constant name, which doesn't depend on how the nested enum type is referenced.
    static boolean rigid(Enum<?> projection) {
        return projection.name().equals("RIGID");
    }

    static Field field(Class<?> owner, String name) throws NoSuchFieldException {
        var field = owner.getDeclaredField(name);
        field.setAccessible(true);
        return field;
    }

    static String box(BoundingBox box) {
        return box.minX() + "," + box.minY() + "," + box.minZ() + "," +
            box.maxX() + "," + box.maxY() + "," + box.maxZ();
    }

    static String position(BlockPos position) {
        return position.getX() + "," + position.getY() + "," + position.getZ();
    }

    // ---------------------------------------------------------------- hashing

    // Block names in y, z, x order, one per line; every kind of air is minecraft:air.
    static String blockNames(ChunkAccess chunk) {
        var names = new StringJoiner("\n");
        var pos = chunk.getPos();
        int minX = pos.x << 4, minZ = pos.z << 4;
        int minY = chunk.getMinY(), maxY = minY + chunk.getHeight();
        for (var y = minY; y < maxY; y++) {
            for (var z = 0; z < 16; z++) {
                for (var x = 0; x < 16; x++) {
                    var state = chunk.getBlockState(new BlockPos(minX + x, y, minZ + z));
                    names.add(state.isAir() ? "minecraft:air"
                        : BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString());
                }
            }
        }
        return names.toString();
    }

    // Quart biome names in y, z, x order, one per line.
    static String biomeNames(ChunkAccess chunk) {
        var names = new StringJoiner("\n");
        int minQuartY = chunk.getMinY() >> 2;
        int maxQuartY = (chunk.getMinY() + chunk.getHeight()) >> 2;
        for (var quartY = minQuartY; quartY < maxQuartY; quartY++)
            for (var quartZ = 0; quartZ < 4; quartZ++)
                for (var quartX = 0; quartX < 4; quartX++)
                    names.add(chunk.getNoiseBiome(quartX, quartY, quartZ).getRegisteredName());
        return names.toString();
    }

    static String sha256(String text) {
        return sha256(text.getBytes(StandardCharsets.UTF_8));
    }

    static String sha256(byte[] bytes) {
        try {
            return HexFormat.of().formatHex(MessageDigest.getInstance("SHA-256").digest(bytes));
        } catch (java.security.NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }
}
