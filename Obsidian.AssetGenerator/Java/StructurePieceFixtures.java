import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.Tag;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.levelgen.structure.StructureStart;
import net.minecraft.world.level.levelgen.structure.pieces.StructurePieceSerializationContext;

/**
 * Records what vanilla saves of structure starts ({@code StructureStart.createTag}, the {@code structures.starts}
 * entries of a chunk) on a headless dedicated server, in a new world of the fixture's seed with structures on.
 *
 * <p>Each case names a start (its structure, dimension and start chunk) and gets {@code fresh}, the start's tag as
 * generated, and with {@code place}, {@code placed}, its tag once every chunk its box reaches is decorated, in x then
 * z order. When loading the last of those tags ({@code StructureStart.loadStaticStart}, as a chunk is loaded) and
 * saving it again gives another tag, that is {@code reloaded}. A tag is kept as its compact SNBT: the start without
 * its pieces, then one string per piece.
 */
public final class StructurePieceFixtures {
    private static MinecraftServer server;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var selected = args.length > 1 ? Set.of(args[1].split("\\+")) : null;
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();

        try {
            startServer(root.get("seed").getAsLong());

            for (var element : root.getAsJsonArray("cases")) {
                var entry = element.getAsJsonObject();
                if (selected != null && !selected.contains(entry.get("name").getAsString()))
                    continue;

                System.out.println("Structure start " + entry.get("name"));
                server.submit(() -> capture(entry)).join();
            }
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }

        root.addProperty("version", server.getServerVersion());
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(fixturePath, gson.toJson(root).replace("\r\n", "\n") + "\n", StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    private static void capture(JsonObject entry) {
        var level = level(entry.get("dimension").getAsString());
        var key = ResourceKey.create(Registries.STRUCTURE, Identifier.parse(entry.get("structure").getAsString()));
        var structure = server.registryAccess().lookupOrThrow(Registries.STRUCTURE).getOrThrow(key).value();
        int chunkX = entry.get("chunkX").getAsInt();
        int chunkZ = entry.get("chunkZ").getAsInt();

        var start = level.getChunk(chunkX, chunkZ, ChunkStatus.STRUCTURE_STARTS, true).getStartForStructure(structure);
        if (start == null || !start.isValid())
            throw new IllegalStateException("No " + key + " starts in chunk " + chunkX + ", " + chunkZ);

        entry.add("fresh", describe(level, start));
        entry.remove("placed");
        entry.remove("reloaded");
        if (!entry.has("place") || !entry.get("place").getAsBoolean()) {
            reload(entry, level, start);
            return;
        }

        // Decorating a chunk places the pieces reaching it, which moves some and records what they placed.
        var box = start.getBoundingBox();
        for (int x = box.minX() >> 4; x <= box.maxX() >> 4; x++) {
            for (int z = box.minZ() >> 4; z <= box.maxZ() >> 4; z++)
                level.getChunk(x, z, ChunkStatus.FEATURES, true);
        }

        entry.add("placed", describe(level, start));
        reload(entry, level, start);
    }

    // What a chunk saves of the start after it was loaded from its last save, when that changes it.
    private static void reload(JsonObject entry, ServerLevel level, StructureStart start) {
        var context = StructurePieceSerializationContext.fromLevel(level);
        var saved = start.createTag(context, start.getChunkPos());
        var loaded = StructureStart.loadStaticStart(context, saved, level.getSeed());
        if (loaded == null || !loaded.isValid())
            throw new IllegalStateException("Vanilla can't load its own save of " + entry.get("name"));

        var reloaded = describe(level, loaded);
        if (!reloaded.equals(describe(level, start)))
            entry.add("reloaded", reloaded);
    }

    private static JsonObject describe(ServerLevel level, StructureStart start) {
        var tag = start.createTag(StructurePieceSerializationContext.fromLevel(level), start.getChunkPos());
        var children = tag.getListOrEmpty("Children");
        var head = tag.copy();
        head.remove("Children");

        var pieces = new JsonArray();
        for (Tag child : children)
            pieces.add(child.toString());

        var result = new JsonObject();
        result.addProperty("start", head.toString());
        result.add("pieces", pieces);
        return result;
    }

    private static ServerLevel level(String dimension) {
        var key = switch (dimension) {
            case "minecraft:overworld" -> Level.OVERWORLD;
            case "minecraft:the_nether" -> Level.NETHER;
            case "minecraft:the_end" -> Level.END;
            default -> throw new IllegalArgumentException(dimension);
        };
        return server.getLevel(key);
    }

    private static void startServer(long seed) throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");

        // No watchdog, since a capture runs on the server thread for as long as it takes.
        var properties = String.join("\n",
            "level-seed=" + seed,
            "generate-structures=true",
            "online-mode=false",
            "server-port=0",
            "max-tick-time=-1",
            "pause-when-empty-seconds=-1",
            "sync-chunk-writes=false",
            "view-distance=2",
            "simulation-distance=2",
            "enable-jmx-monitoring=false",
            "enable-rcon=false",
            "enable-query=false",
            "");
        Files.writeString(Path.of("server.properties"), properties);

        net.minecraft.server.Main.main(new String[]{"--nogui"});

        // Main keeps the server only in its shutdown hook, so find it there.
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
}
