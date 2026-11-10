import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.lang.reflect.Field;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.TreeSet;
import java.util.stream.Collectors;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.LightLayer;
import net.minecraft.world.level.biome.Biome;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.chunk.LevelChunk;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.levelgen.structure.BoundingBox;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.ticks.LevelChunkTicks;
import net.minecraft.world.ticks.ScheduledTick;

/**
 * Records ice, frosted ice and snow layers melting, and the precipitation pass of the chunk tick (snow building up,
 * water freezing, cauldrons filling), on a headless dedicated server of its own in a normal overworld (sea level 63,
 * which the snow line is measured from) whose game is frozen.
 *
 * <p>Every case uses the three by three chunks around chunk (0, 0), which are cleared first. Biomes are filled over
 * those chunks with the case's biome, and its blocks are set without updates. The probe then waits for the light
 * engine and records the light the behaviour reads, so that the replay can take light as an input:
 *
 * <ul>
 *   <li>{@code random_tick}: the target's {@code BlockState.randomTick} with the level random seeded with the case's
 *   seed.</li>
 *   <li>{@code tick}: the target's scheduled {@code BlockState.tick}, likewise.</li>
 *   <li>{@code place}: the case's {@code place} state set at the target with every update, after seeding.</li>
 *   <li>{@code precipitation}: {@code ServerLevel.tickChunk} on chunk (0, 0) {@code ticks} times at {@code speed},
 *   with the level random seeded and {@code randValue} set. Its layers are one state over the nine chunks, or tiled
 *   from the absolute x and z; the light of chunk (0, 0) is recorded at each height they reach.</li>
 * </ul>
 *
 * <p>Each case records the blocks that changed (absolute positions, full states), the block and fluid ticks scheduled
 * in the nine chunks with their delays, the level random's next {@code nextInt()} (which shows the draws made) and,
 * for precipitation, {@code randValue} afterwards.
 */
public final class MeltingWeatherFixtures {
    // Set without neighbor or shape updates, drops or onPlace, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // The nine chunks around chunk (0, 0).
    private static final int MinBlock = -16;
    private static final int MaxBlock = 31;

    // How far around a single block's target its changes are compared.
    private static final int Reach = 2;

    private static MinecraftServer server;

    // Positions each level's cases wrote, set back to air before the next case there.
    private static final Map<ResourceKey<Level>, Set<BlockPos>> written = new java.util.HashMap<>();

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();

        try {
            startServer(args.length > 2 && args[2].equals("flat"));
            server.submit(MeltingWeatherFixtures::configure).join();
            var selected = args.length > 1 ? Set.of(args[1].split("\\+")) : null;
            var captured = 0;
            for (var element : root.getAsJsonArray("cases")) {
                var testCase = element.getAsJsonObject();
                if (selected == null || selected.contains(testCase.get("name").getAsString())) {
                    capture(testCase);
                    captured++;
                }
            }
            if (selected != null && captured != selected.size())
                throw new IllegalArgumentException("Every selected case must exist and be named once.");
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }

        root.addProperty("version", server.getServerVersion());
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(fixturePath, compact(gson.toJson(root)) + "\n", StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    // Nothing moves on its own: the game is frozen, and time, weather, spawning and random ticks are off.
    private static void configure() {
        server.tickRateManager().setFrozen(true);
        var rules = server.getGameRules();
        rules.set(GameRules.ADVANCE_TIME, false, server);
        rules.set(GameRules.ADVANCE_WEATHER, false, server);
        rules.set(GameRules.SPAWN_MOBS, false, server);
        rules.set(GameRules.RANDOM_TICK_SPEED, 0, server);
    }

    private static void capture(JsonObject testCase) throws Exception {
        var level = server.getLevel(dimension(testCase));
        server.submit(() -> prepare(level, testCase)).join();
        waitForLight(level);
        server.submit(() -> run(level, testCase)).join();
        System.out.println("Captured " + testCase.get("name").getAsString());
    }

    private static ResourceKey<Level> dimension(JsonObject testCase) {
        var name = testCase.has("dimension") ? testCase.get("dimension").getAsString() : "overworld";
        return name.equals("nether") ? Level.NETHER : Level.OVERWORLD;
    }

    private static void prepare(ServerLevel level, JsonObject testCase) {
        clear(level);
        fillBiome(level, testCase);

        var positions = written.get(level.dimension());
        var kind = testCase.get("kind").getAsString();
        if (kind.equals("precipitation")) {
            for (var element : testCase.getAsJsonArray("layers"))
                setLayer(level, element.getAsJsonObject(), positions);
        } else {
            for (var element : testCase.getAsJsonArray("setup")) {
                var placement = element.getAsJsonObject();
                var pos = position(placement.getAsJsonArray("pos"));
                level.setBlock(pos, state(placement.get("state").getAsString()), QuietFlags);
                positions.add(pos);
            }
        }

        var raining = testCase.has("raining") && testCase.get("raining").getAsBoolean();
        var weather = level.getWeatherData();
        weather.setRaining(raining);
        weather.setThundering(false);
        level.setRainLevel(raining ? 1.0F : 0.0F);
        level.setThunderLevel(0.0F);

        var dayTime = testCase.has("dayTime") ? testCase.get("dayTime").getAsInt() : 6000;
        command("time set " + dayTime);

        var maxSnow = testCase.has("maxSnowAccumulationHeight")
            ? testCase.get("maxSnowAccumulationHeight").getAsInt()
            : 1;
        server.getGameRules().set(GameRules.MAX_SNOW_ACCUMULATION_HEIGHT, maxSnow, server);
    }

    // The first time a level is used, everything above the cases' floor goes; after that, what earlier cases wrote.
    private static void clear(ServerLevel level) {
        var positions = written.get(level.dimension());
        if (positions == null) {
            positions = new HashSet<>();
            written.put(level.dimension(), positions);
            loadArea(level);

            var bottom = level.dimension() == Level.NETHER ? 20 : 40;
            // A flat world's grass is below this probe's usual floor and would add unrelated random-tick sections.
            if (level.getSeaLevel() == -63)
                bottom = level.getMinY();
            var top = level.dimension() == Level.NETHER ? 120 : 260;
            for (var x = MinBlock; x <= MaxBlock; x++) {
                for (var z = MinBlock; z <= MaxBlock; z++) {
                    for (var y = bottom; y <= top; y++)
                        level.setBlock(new BlockPos(x, y, z), Blocks.AIR.defaultBlockState(), QuietFlags);
                }
            }
        }

        for (var pos : positions)
            level.setBlock(pos, Blocks.AIR.defaultBlockState(), QuietFlags);

        positions.clear();
        var box = new BoundingBox(MinBlock, level.getMinY(), MinBlock, MaxBlock, level.getMaxY(), MaxBlock);
        level.getBlockTicks().clearArea(box);
        level.getFluidTicks().clearArea(box);
    }

    private static void loadArea(ServerLevel level) {
        for (var x = -1; x <= 1; x++) {
            for (var z = -1; z <= 1; z++)
                level.getChunk(x, z);
        }
    }

    private static void fillBiome(ServerLevel level, JsonObject testCase) {
        var name = testCase.get("biome").getAsString();
        var key = ResourceKey.create(Registries.BIOME, Identifier.parse(name));
        Holder<Biome> biome = level.registryAccess().lookupOrThrow(Registries.BIOME).getOrThrow(key);
        var border = testCase.has("biomeBorder") ? testCase.getAsJsonObject("biomeBorder") : null;
        var otherKey = border == null ? key
            : ResourceKey.create(Registries.BIOME, Identifier.parse(border.get("other").getAsString()));
        var other = level.registryAccess().lookupOrThrow(Registries.BIOME).getOrThrow(otherKey);
        for (var x = -1; x <= 1; x++) {
            for (var z = -1; z <= 1; z++) {
                var chunk = level.getChunk(x, z);
                chunk.fillBiomesFromNoise((qx, qy, qz) -> {
                    if (border == null)
                        return biome;

                    var coordinate = switch (border.get("axis").getAsString()) {
                        case "x" -> qx;
                        case "y" -> qy;
                        case "z" -> qz;
                        default -> throw new IllegalArgumentException("Unknown biome border axis.");
                    };
                    return coordinate >= border.get("quart").getAsInt() ? other : biome;
                });
                chunk.markUnsaved();
            }
        }
    }

    // A layer is one state over the nine chunks, or a tile of palette symbols repeated from x = 0 and z = 0: the row
    // is z modulo the rows, the column x modulo the row's length.
    private static void setLayer(ServerLevel level, JsonObject layer, Set<BlockPos> positions) {
        var y = layer.get("y").getAsInt();
        for (var x = MinBlock; x <= MaxBlock; x++) {
            for (var z = MinBlock; z <= MaxBlock; z++) {
                var state = state(layerState(layer, x, z));
                var pos = new BlockPos(x, y, z);
                level.setBlock(pos, state, QuietFlags);
                positions.add(pos);
            }
        }

        // What the case grows or freezes above or below the layer is cleared with it.
        for (var x = MinBlock; x <= MaxBlock; x++) {
            for (var z = MinBlock; z <= MaxBlock; z++) {
                for (var dy = -1; dy <= 3; dy++)
                    positions.add(new BlockPos(x, y + dy, z));
            }
        }
    }

    private static String layerState(JsonObject layer, int x, int z) {
        if (layer.has("state"))
            return layer.get("state").getAsString();

        var tile = layer.getAsJsonArray("tile");
        var row = tile.get(Math.floorMod(z, tile.size())).getAsString();
        var symbol = String.valueOf(row.charAt(Math.floorMod(x, row.length())));
        return layer.getAsJsonObject("palette").get(symbol).getAsString();
    }

    // Light updates run on the light engine's own thread between ticks; wait until it has nothing left for the area.
    private static void waitForLight(ServerLevel level) throws Exception {
        var engine = level.getChunkSource().getLightEngine();
        for (var attempt = 0; attempt < 2400; attempt++) {
            var pending = server.submit(() -> {
                engine.tryScheduleUpdate();
                return engine.waitForPendingTasks(0, 0);
            }).join();
            pending.join();

            boolean done = server.submit(() -> !engine.hasLightWork()).join();
            if (done && attempt > 1)
                return;

            Thread.sleep(50);
        }

        throw new IllegalStateException("The light engine didn't settle.");
    }

    private static void run(ServerLevel level, JsonObject testCase) {
        if (testCase.has("generator"))
            testCase.addProperty("seaLevel", level.getSeaLevel());

        level.updateSkyBrightness();
        testCase.addProperty("skyDarken", level.getSkyDarken());

        var kind = testCase.get("kind").getAsString();
        if (kind.equals("precipitation")) {
            runPrecipitation(level, testCase);
            return;
        }

        var target = position(testCase.getAsJsonArray("target"));
        var light = new JsonArray();
        for (var element : testCase.getAsJsonArray("setup"))
            light.add(lightAt(level, position(element.getAsJsonObject().getAsJsonArray("pos"))));

        testCase.add("light", light);

        var before = snapshot(level, target);
        level.getRandom().setSeed(testCase.get("seed").getAsLong());
        var state = level.getBlockState(target);
        switch (kind) {
            case "random_tick" -> state.randomTick(level, target, level.getRandom());
            case "tick" -> state.tick(level, target, level.getRandom());
            case "place" -> level.setBlock(target, state(testCase.get("place").getAsString()), Block.UPDATE_ALL);
            default -> throw new IllegalArgumentException("Unknown kind " + kind);
        }

        testCase.addProperty("nextRandom", level.getRandom().nextInt());
        testCase.add("changes", changes(level, before));
        testCase.add("blockTicks", blockTicks(level));
        testCase.add("fluidTicks", fluidTicks(level));
        written.get(level.dimension()).addAll(before.keySet());
    }

    private static void runPrecipitation(ServerLevel level, JsonObject testCase) {
        var layers = testCase.getAsJsonArray("layers");
        var lowest = Integer.MAX_VALUE;
        var highest = Integer.MIN_VALUE;
        for (var element : layers) {
            var y = element.getAsJsonObject().get("y").getAsInt();
            lowest = Math.min(lowest, y);
            highest = Math.max(highest, y);
        }

        // The light of chunk (0, 0) at each height from just below the layers to the snow that can grow on them.
        var light = new JsonArray();
        for (var y = lowest - 1; y <= highest + 3; y++)
            light.add(layerLight(level, y));

        testCase.add("light", light);

        var before = new java.util.HashMap<BlockPos, BlockState>();
        for (var x = 0; x < 16; x++) {
            for (var z = 0; z < 16; z++) {
                for (var y = lowest - 1; y <= highest + 3; y++) {
                    var pos = new BlockPos(x, y, z);
                    before.put(pos, level.getBlockState(pos));
                }
            }
        }

        if (testCase.has("biomeBorder")) {
            testCase.addProperty("biomeSeed", level.getSeed());
            var samples = new JsonArray();
            for (var z = 0; z < 16; z++) {
                for (var x = 0; x < 16; x++) {
                    var pos = new BlockPos(x, highest + 1, z);
                    var selected = level.getBiome(pos);
                    var sample = new JsonObject();
                    sample.add("pos", array(pos));
                    sample.addProperty("biome", selected.unwrapKey().orElseThrow().identifier().toString());
                    samples.add(sample);
                }
            }
            testCase.add("biomeSamples", samples);
        }

        level.getRandom().setSeed(testCase.get("seed").getAsLong());
        setRandValue(level, testCase.get("randValue").getAsInt());

        var chunk = level.getChunk(0, 0);
        var speed = testCase.get("speed").getAsInt();
        var ticks = testCase.get("ticks").getAsInt();
        for (var tick = 0; tick < ticks; tick++)
            level.tickChunk(chunk, speed);

        testCase.addProperty("nextRandom", level.getRandom().nextInt());
        testCase.addProperty("randValueAfter", randValue(level));
        testCase.add("changes", changes(level, before));
        testCase.add("blockTicks", blockTicks(level));
        testCase.add("fluidTicks", fluidTicks(level));
    }

    private static JsonObject lightAt(ServerLevel level, BlockPos pos) {
        var light = new JsonObject();
        light.add("pos", array(pos));
        light.addProperty("block", level.getBrightness(LightLayer.BLOCK, pos));
        light.addProperty("sky", level.getBrightness(LightLayer.SKY, pos));
        return light;
    }

    // The light of chunk (0, 0) at a height: a hexadecimal digit per column, z * 16 + x.
    private static JsonObject layerLight(ServerLevel level, int y) {
        var block = new StringBuilder();
        var sky = new StringBuilder();
        for (var z = 0; z < 16; z++) {
            for (var x = 0; x < 16; x++) {
                var pos = new BlockPos(x, y, z);
                block.append(Integer.toHexString(level.getBrightness(LightLayer.BLOCK, pos)));
                sky.append(Integer.toHexString(level.getBrightness(LightLayer.SKY, pos)));
            }
        }

        var light = new JsonObject();
        light.addProperty("y", y);
        light.addProperty("block", block.toString());
        light.addProperty("sky", sky.toString());
        return light;
    }

    private static Map<BlockPos, BlockState> snapshot(ServerLevel level, BlockPos target) {
        var states = new java.util.HashMap<BlockPos, BlockState>();
        for (var pos : BlockPos.betweenClosed(target.offset(-Reach, -Reach, -Reach), target.offset(Reach, Reach, Reach)))
            states.put(pos.immutable(), level.getBlockState(pos));

        return states;
    }

    private static JsonArray changes(ServerLevel level, Map<BlockPos, BlockState> before) {
        var changes = new TreeSet<String>();
        for (var entry : before.entrySet()) {
            var now = level.getBlockState(entry.getKey());
            if (now != entry.getValue())
                changes.add(key(entry.getKey()) + "=" + describe(now));
        }

        return strings(changes);
    }

    @SuppressWarnings("unchecked")
    private static JsonArray blockTicks(ServerLevel level) {
        var ticks = new TreeSet<String>();
        for (var chunk : areaChunks(level)) {
            var container = (LevelChunkTicks<Block>) chunk.getBlockTicks();
            container.getAll().forEach(tick -> ticks.add(describeTick(level, tick,
                BuiltInRegistries.BLOCK.getKey(tick.type()).toString())));
        }

        return strings(ticks);
    }

    @SuppressWarnings("unchecked")
    private static JsonArray fluidTicks(ServerLevel level) {
        var ticks = new TreeSet<String>();
        for (var chunk : areaChunks(level)) {
            var container = (LevelChunkTicks<Fluid>) chunk.getFluidTicks();
            container.getAll().forEach(tick -> ticks.add(describeTick(level, tick,
                BuiltInRegistries.FLUID.getKey(tick.type()).toString())));
        }

        return strings(ticks);
    }

    private static List<LevelChunk> areaChunks(ServerLevel level) {
        var chunks = new ArrayList<LevelChunk>();
        for (var x = -1; x <= 1; x++) {
            for (var z = -1; z <= 1; z++)
                chunks.add(level.getChunk(x, z));
        }

        return chunks;
    }

    private static String describeTick(ServerLevel level, ScheduledTick<?> tick, String type) {
        var delay = tick.triggerTick() - level.getGameTime();
        return key(tick.pos()) + "=" + type + "@" + delay;
    }

    private static String key(BlockPos pos) {
        return pos.getX() + "," + pos.getY() + "," + pos.getZ();
    }

    // A state with every property, sorted by name.
    private static String describe(BlockState state) {
        var name = BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString();
        if (state.getProperties().isEmpty())
            return name;

        var properties = state.getProperties().stream()
            .sorted((a, b) -> a.getName().compareTo(b.getName()))
            .map(property -> property.getName() + "=" + valueName(state, property))
            .collect(Collectors.joining(","));
        return name + "[" + properties + "]";
    }

    private static <T extends Comparable<T>> String valueName(BlockState state, Property<T> property) {
        return property.getName(state.getValue(property));
    }

    private static void setRandValue(Level level, int value) {
        try {
            randValueField().setInt(level, value);
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException(e);
        }
    }

    private static int randValue(Level level) {
        try {
            return randValueField().getInt(level);
        } catch (ReflectiveOperationException e) {
            throw new IllegalStateException(e);
        }
    }

    private static Field randValueField() throws NoSuchFieldException {
        var field = Level.class.getDeclaredField("randValue");
        field.setAccessible(true);
        return field;
    }

    private static void command(String command) {
        server.getCommands().performPrefixedCommand(server.createCommandSourceStack(), command);
    }

    private static JsonArray strings(Iterable<String> values) {
        var array = new JsonArray();
        for (var value : values)
            array.add(value);

        return array;
    }

    private static JsonArray array(BlockPos pos) {
        var array = new JsonArray();
        array.add(pos.getX());
        array.add(pos.getY());
        array.add(pos.getZ());
        return array;
    }

    private static BlockState state(String text) {
        var bracket = text.indexOf('[');
        var name = bracket < 0 ? text : text.substring(0, bracket);
        var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(name));
        var state = block.defaultBlockState();
        if (bracket >= 0) {
            for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
                var parts = pair.split("=");
                state = withValue(state, block.getStateDefinition().getProperty(parts[0]), parts[1]);
            }
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState withValue(BlockState state, Property<T> property,
        String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    // Keeps each short array of numbers, and each object of plain values, on one line.
    private static String compact(String json) {
        var numbers = java.util.regex.Pattern.compile("\\[\\s*(-?[\\d.]+(?:,\\s*-?[\\d.]+)*)\\s*\\]");
        var matcher = numbers.matcher(json.replace("\r\n", "\n"));
        var result = new StringBuilder();
        while (matcher.find()) {
            var inner = matcher.group(1).replaceAll("\\s+", "").replace(",", ", ");
            matcher.appendReplacement(result, java.util.regex.Matcher.quoteReplacement("[" + inner + "]"));
        }

        matcher.appendTail(result);

        // An object whose every line is a key with a value that fits on that line.
        var objects = java.util.regex.Pattern.compile("\\{\\n((?: *\"[^\\n{}]*\\n)+) *\\}");
        matcher = objects.matcher(result.toString());
        var joined = new StringBuilder();
        while (matcher.find()) {
            var inner = matcher.group(1).trim().replaceAll("\\n *", " ");
            matcher.appendReplacement(joined, java.util.regex.Matcher.quoteReplacement("{" + inner + "}"));
        }

        matcher.appendTail(joined);
        return joined.toString();
    }

    private static void startServer(boolean flat) throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");

        // The generator is part of the input: its sea level sets the snow line. Nothing spawns and the watchdog is
        // disabled because each bounded case runs as one task on the server thread.
        var properties = String.join("\n",
            "level-seed=melting-weather",
            "level-type=" + (flat ? "minecraft:flat" : "minecraft:normal"),
            "generate-structures=false",
            "spawn-protection=0",
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
