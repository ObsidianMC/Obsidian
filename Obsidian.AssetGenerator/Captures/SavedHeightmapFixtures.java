import com.google.gson.*;
import java.io.*;
import java.nio.file.*;
import java.util.*;
import java.util.zip.GZIPOutputStream;
import net.minecraft.core.BlockPos;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.chunk.ChunkAccess;
import net.minecraft.world.level.chunk.status.ChunkStatus;
import net.minecraft.world.level.chunk.storage.SerializableChunkData;

/** Captures a natural chunk before decoration and the full chunk's serialized heightmaps.
 * Run with CaptureVanillaScenarios -p:CaptureFixtures=saved_heightmaps.
 */
public final class SavedHeightmapFixtures {
    private static MinecraftServer server;

    public static void main(String[] args) throws Exception {
        var path = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        try {
            startServer(root.get("seed").getAsLong(), root.get("structures").getAsBoolean());
            server.submit(() -> {
                try {
                    capture(root);
                } catch (Exception error) {
                    throw new RuntimeException(error);
                }
            }).join();
            root.addProperty("version", server.getServerVersion());
            var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
            Files.writeString(path, gson.toJson(root).replace("\r\n", "\n") + "\n");
            System.out.println("Wrote " + path);
            Runtime.getRuntime().halt(0);
        } catch (Throwable error) {
            error.printStackTrace();
            Runtime.getRuntime().halt(1);
        }
    }

    private static void capture(JsonObject root) throws Exception {
        var level = server.overworld();
        for (var value : root.getAsJsonArray("cases")) {
            var entry = value.getAsJsonObject();
            int x = entry.get("chunkX").getAsInt();
            int z = entry.get("chunkZ").getAsInt();
            var terrain = level.getChunkSource().getChunk(x, z, ChunkStatus.TERRAIN, true);
            if (terrain.getPersistedStatus() != ChunkStatus.TERRAIN) {
                throw new IllegalStateException("Capture target was already decorated");
            }
            entry.addProperty("minY", terrain.getMinY());
            entry.addProperty("height", terrain.getHeight());
            entry.addProperty("terrain", blocks(terrain));
            var full = level.getChunkSource().getChunk(x, z, ChunkStatus.FULL, true);
            entry.addProperty("full", blocks(full));
            var saved = SerializableChunkData.copyOf(level, full).write();
            entry.addProperty("status", saved.getString("Status").orElseThrow());
            var tag = saved.getCompound("Heightmaps").orElseThrow();
            var maps = new JsonObject();
            for (var type : ChunkStatus.FULL.heightmapsAfter()) {
                var words = new JsonArray();
                for (long word : tag.getLongArray(type.getSerializationKey()).orElseThrow()) {
                    words.add(word);
                }
                maps.add(type.getSerializationKey(), words);
            }
            entry.add("heightmaps", maps);
        }
    }

    // State ids are the pinned version's registry ids. Traversal: ascending y, z, x; big endian int32, gzip/base64.
    private static String blocks(ChunkAccess chunk) throws Exception {
        var bytes = new ByteArrayOutputStream();
        try (var output = new DataOutputStream(new GZIPOutputStream(bytes))) {
            var position = new BlockPos.MutableBlockPos();
            for (int y = chunk.getMinY(); y <= chunk.getMaxY(); y++) {
                for (int z = 0; z < 16; z++) {
                    for (int x = 0; x < 16; x++) {
                        position.set(chunk.getPos().getMinBlockX() + x, y, chunk.getPos().getMinBlockZ() + z);
                        output.writeInt(Block.getId(chunk.getBlockState(position)));
                    }
                }
            }
        }
        return Base64.getEncoder().encodeToString(bytes.toByteArray());
    }

    private static void startServer(long seed, boolean structures) throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");
        Files.writeString(Path.of("server.properties"), String.join("\n",
            "level-seed=" + seed,
            "generate-structures=" + structures,
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
            ""));
        net.minecraft.server.Main.main(new String[]{"--nogui"});

        var hooksField = Class.forName("java.lang.ApplicationShutdownHooks").getDeclaredField("hooks");
        hooksField.setAccessible(true);
        @SuppressWarnings("unchecked")
        var hooks = (Map<Thread, Thread>) hooksField.get(null);
        for (var hook : new ArrayList<>(hooks.keySet())) {
            if (hook.getClass().getName().equals("net.minecraft.server.Main$1")) {
                var field = hook.getClass().getDeclaredField("val$dedicatedServer");
                field.setAccessible(true);
                server = (MinecraftServer)field.get(hook);
            }
        }

        if (server == null) {
            throw new IllegalStateException("The dedicated server did not start.");
        }

        while (!server.isReady()) {
            Thread.sleep(50);
        }
    }
}
