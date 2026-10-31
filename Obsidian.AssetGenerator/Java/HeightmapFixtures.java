import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Map;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.levelgen.Heightmap;

/** Records native runtime chunk edits, packed heightmaps and every vanilla state predicate. */
public final class HeightmapFixtures {
    private static MinecraftServer server;

    public static void main(String[] args) throws Exception {
        var path = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        try {
            startServer();
            server.submit(() -> capture(root)).join();
            root.addProperty("version", server.getServerVersion());
            var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
            Files.writeString(path, gson.toJson(root).replace("\r\n", "\n") + "\n");
            System.out.println("Wrote " + path);
            System.out.flush();
            Runtime.getRuntime().halt(0);
        } catch (Throwable error) {
            error.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }
    }

    private static void capture(JsonObject root) {
        var predicates = new JsonObject();
        for (var type : Heightmap.Types.values()) {
            var bits = new StringBuilder();
            for (var state : Block.BLOCK_STATE_REGISTRY) {
                bits.append(type.isOpaque().test(state) ? '1' : '0');
            }

            predicates.addProperty(type.name(), bits.toString());
        }

        root.add("predicates", predicates);
        for (var value : root.getAsJsonArray("cases")) {
            var input = value.getAsJsonObject();
            var level = server.getLevel(input.get("dimension").getAsString().equals("nether")
                ? Level.NETHER : Level.OVERWORLD);
            var chunk = new LevelChunk(level, new ChunkPos(-3, 2));
            input.addProperty("minY", chunk.getMinY());
            input.addProperty("height", chunk.getHeight());
            input.add("initial", describe(chunk, 0, 0));
            for (var step : input.getAsJsonArray("steps")) {
                var action = step.getAsJsonObject();
                var pos = action.getAsJsonArray("pos");
                var position = new BlockPos(pos.get(0).getAsInt(), pos.get(1).getAsInt(), pos.get(2).getAsInt());
                var state = state(action.get("state").getAsString());
                chunk.setBlockState(position, state, 0);
                action.add("heightmaps", describe(chunk, position.getX() & 15, position.getZ() & 15));
            }
        }
    }

    private static JsonObject describe(LevelChunk chunk, int x, int z) {
        var result = new JsonObject();
        for (var type : Heightmap.Types.values()) {
            if (type == Heightmap.Types.WORLD_SURFACE_WG || type == Heightmap.Types.OCEAN_FLOOR_WG) {
                continue;
            }

            var packed = new JsonArray();
            for (long word : chunk.getOrCreateHeightmapUnprimed(type).getRawData()) {
                packed.add(word);
            }

            var map = new JsonObject();
            map.addProperty("height", chunk.getHeight(type, x, z) + 1);
            map.add("packed", packed);
            result.add(type.name(), map);
        }

        return result;
    }

    private static BlockState state(String text) {
        int bracket = text.indexOf('[');
        var name = bracket < 0 ? text : text.substring(0, bracket);
        var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(name));
        var state = block.defaultBlockState();
        if (bracket >= 0) {
            for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
                var parts = pair.split("=");
                state = property(state, block.getStateDefinition().getProperty(parts[0]), parts[1]);
            }
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState property(BlockState state, Property<T> property, String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    private static void startServer() throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");
        Files.writeString(Path.of("server.properties"), String.join("\n",
            "level-seed=123",
            "generate-structures=false",
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
