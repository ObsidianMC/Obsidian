import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import io.netty.channel.ChannelFutureListener;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.TreeMap;
import java.util.UUID;
import java.util.regex.Pattern;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.ByteTag;
import net.minecraft.nbt.IntTag;
import net.minecraft.nbt.LongTag;
import net.minecraft.nbt.StringTag;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundDisguisedChatPacket;
import net.minecraft.network.protocol.game.ClientboundPlayerChatPacket;
import net.minecraft.network.protocol.game.ClientboundSystemChatPacket;
import net.minecraft.network.protocol.game.ServerboundSetCommandBlockPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.players.NameAndId;
import net.minecraft.server.players.PlayerList;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.CommandBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;

/**
 * Records command blocks tick by tick, on a headless dedicated server in a flat world of air whose game is frozen and
 * stepped one tick at a time.
 *
 * <p>Each case of the fixture is its input: blocks set up around the origin, command blocks with their command,
 * {@code auto} and {@code TrackOutput}, game rules, and actions before given ticks: a block set with every update, or
 * an edit sent through vanilla's own {@code handleSetCommandBlock} by an operator in creative mode. The setup is set
 * without updates, and the command blocks are given their fields the way loading their data would, so a repeating
 * block set to always active schedules its first tick then. After each tick the probe records the area's blocks that
 * changed since the setup, every command block's {@code SuccessCount}, power, condition, {@code auto} and
 * {@code LastOutput} (without its wall-clock time), and the chat the operator received, as text; after the last tick,
 * what each command block saves (what an operator's editor is sent), with {@code LastExecution} relative to the game
 * time before the first tick.
 */
public final class CommandBlockFixtures {
    // The compared area around the origin.
    private static final int MinX = -2;
    private static final int MaxX = 5;
    private static final int MinY = -1;
    private static final int MaxY = 3;
    private static final int MinZ = -2;
    private static final int MaxZ = 3;

    // Ticks run after a case's area is cleared, so that its pending ticks find air and are dropped.
    private static final int DrainTicks = 3;

    // Set without neighbor or shape updates and without drops, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // The game rules cases change, with the values every case starts from.
    private static final Map<String, String> DefaultRules = Map.of(
        "command_block_output", "true",
        "send_command_feedback", "true",
        "command_blocks_work", "true",
        "max_command_sequence_length", "65536");

    // The wall-clock time BaseCommandBlock puts before each output.
    private static final Pattern Time = Pattern.compile("^\\[\\d\\d:\\d\\d:\\d\\d\\] ");

    private static MinecraftServer server;

    private static BlockPos origin;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();
        var selected = args.length > 1 && !args[1].isBlank() ? Set.of(args[1].split("\\+")) : Set.<String>of();

        try {
            startServer();
            origin = position(root.getAsJsonArray("origin"));
            forceArea();
            capture(root, selected);
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

    // Forces the origin's chunk and its neighbours and waits until they tick blocks, then freezes the game.
    private static void forceArea() throws Exception {
        var level = server.overworld();
        server.submit(() -> {
            for (int x = (origin.getX() >> 4) - 1; x <= (origin.getX() >> 4) + 1; x++) {
                for (int z = (origin.getZ() >> 4) - 1; z <= (origin.getZ() >> 4) + 1; z++)
                    level.setChunkForced(x, z, true);
            }
        }).join();

        var chunk = net.minecraft.world.level.ChunkPos.containing(origin).pack();
        while (!server.submit(() -> level.shouldTickBlocksAt(chunk)).join())
            Thread.sleep(50);

        server.submit(() -> server.tickRateManager().setFrozen(true)).join();
    }

    private static void capture(JsonObject root, Set<String> selected) throws Exception {
        var level = server.overworld();

        // An operator in creative mode, out of the area, who hears the chat and edits the command blocks.
        var operator = new ServerPlayer(server, level, profile("Operator"), ClientInformation.createDefault());
        var heard = new ArrayList<Packet<?>>();
        operator.connection = recorder(heard, operator);
        server.submit(() -> {
            operator.snapTo(origin.getX() + 0.5, origin.getY(), origin.getZ() - 6.5, 0, 0);
            try {
                players(server.getPlayerList()).add(operator);
            } catch (Exception e) {
                throw new RuntimeException(e);
            }

            level.players().add(operator);
            server.getPlayerList().op(new NameAndId(operator.getGameProfile()));
            operator.setGameMode(GameType.CREATIVE);
        }).join();

        var count = 0;
        for (var element : root.getAsJsonArray("cases")) {
            var input = element.getAsJsonObject();
            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            server.submit(() -> clear(level)).join();
            for (int drain = 0; drain < DrainTicks; drain++)
                step(level);

            setRules(input);
            var before = server.submit(() -> setUp(level, input)).join();
            var start = server.submit(level::getGameTime).join();
            heard.clear();

            var checkpoints = new JsonArray();
            var ticks = input.get("ticks").getAsInt();
            for (int tick = 1; tick <= ticks; tick++) {
                var number = tick;
                server.submit(() -> act(level, operator, input, number)).join();
                step(level);
                var checkpoint = server.submit(() -> checkpoint(level, number, before, heard)).join();
                heard.clear();
                checkpoints.add(checkpoint);
            }

            input.add("checkpoints", checkpoints);
            input.add("saved", server.submit(() -> saved(level, start)).join());
            count++;
        }

        server.submit(() -> clear(level)).join();
        System.out.println("Ran " + count + " command block cases");
    }

    // Every rule a case can change goes back to its default first, then the case's own are set.
    private static void setRules(JsonObject input) {
        var rules = new TreeMap<>(DefaultRules);
        if (input.has("rules")) {
            for (var rule : input.getAsJsonObject("rules").entrySet())
                rules.put(rule.getKey(), rule.getValue().getAsString());
        }

        for (var rule : rules.entrySet()) {
            var command = "gamerule minecraft:" + rule.getKey() + " " + rule.getValue();
            server.submit(() -> server.getCommands().performPrefixedCommand(
                server.createCommandSourceStack().withSuppressedOutput(), command)).join();
        }
    }

    // The case's blocks and command blocks in the cleared area; returns the area's states before the first tick.
    private static List<BlockState> setUp(ServerLevel level, JsonObject input) {
        if (input.has("setup")) {
            for (var setup : input.getAsJsonArray("setup")) {
                var placement = setup.getAsJsonObject();
                var pos = origin.offset(position(placement.getAsJsonArray("offset")));
                level.setBlock(pos, state(placement.get("state").getAsString()), QuietFlags);
            }
        }

        for (var value : input.getAsJsonArray("commandBlocks")) {
            var definition = value.getAsJsonObject();
            var pos = origin.offset(position(definition.getAsJsonArray("offset")));
            level.setBlock(pos, state(definition.get("state").getAsString()), QuietFlags);

            // The fields in the order CommandBlockEntity.loadAdditional reads them: auto last, as setAutomatic,
            // which schedules an always active block's first tick.
            var entity = (CommandBlockEntity) level.getBlockEntity(pos);
            entity.getCommandBlock().setCommand(definition.get("command").getAsString());
            var track = !definition.has("trackOutput") || definition.get("trackOutput").getAsBoolean();
            entity.getCommandBlock().setTrackOutput(track);
            entity.setAutomatic(definition.has("auto") && definition.get("auto").getAsBoolean());
        }

        return snapshot(level);
    }

    // The actions due before the tick: a block set with every update, or an edit through the packet's handler.
    private static void act(ServerLevel level, ServerPlayer operator, JsonObject input, int tick) {
        if (!input.has("actions"))
            return;

        for (var value : input.getAsJsonArray("actions")) {
            var action = value.getAsJsonObject();
            if (action.get("tick").getAsInt() != tick)
                continue;

            if (action.has("edit")) {
                edit(operator, action.getAsJsonObject("edit"));
                continue;
            }

            var pos = origin.offset(position(action.getAsJsonArray("offset")));
            level.setBlock(pos, state(action.get("state").getAsString()), Block.UPDATE_ALL);
        }
    }

    private static void edit(ServerPlayer operator, JsonObject edit) {
        var pos = origin.offset(position(edit.getAsJsonArray("offset")));
        var mode = CommandBlockEntity.Mode.valueOf(edit.get("mode").getAsString().toUpperCase(Locale.ROOT));
        var track = !edit.has("trackOutput") || edit.get("trackOutput").getAsBoolean();
        var conditional = edit.has("conditional") && edit.get("conditional").getAsBoolean();
        var automatic = edit.has("auto") && edit.get("auto").getAsBoolean();
        var packet = new ServerboundSetCommandBlockPacket(pos, edit.get("command").getAsString(), mode, track,
            conditional, automatic);
        operator.connection.handleSetCommandBlock(packet);
    }

    // Runs one game tick and waits until the server has finished it.
    private static void step(ServerLevel level) throws Exception {
        long before = server.submit(level::getGameTime).join();
        server.submit(() -> server.tickRateManager().stepGameIfPaused(1)).join();
        while (server.submit(level::getGameTime).join() == before) {
            if (!server.isRunning())
                throw new IllegalStateException("The server stopped.");

            Thread.sleep(1);
        }
    }

    private static JsonObject checkpoint(ServerLevel level, int tick, List<BlockState> before, List<Packet<?>> heard) {
        var checkpoint = new JsonObject();
        checkpoint.addProperty("tick", tick);
        var changes = changes(level, before);
        if (!changes.isEmpty())
            checkpoint.addProperty("changes", changes);

        var blocks = new JsonArray();
        for (var pos : compared()) {
            if (level.getBlockEntity(pos) instanceof CommandBlockEntity entity)
                blocks.add(describe(pos, entity));
        }

        checkpoint.add("commandBlocks", blocks);
        var chat = new JsonArray();
        for (var packet : heard) {
            var text = chat(packet);
            if (text != null)
                chat.add(text);
        }

        if (!chat.isEmpty())
            checkpoint.add("chat", chat);

        return checkpoint;
    }

    // "x,y,z success=N powered=b conditionMet=b auto=b output=<text>", the output without its time; "-" for none.
    private static String describe(BlockPos pos, CommandBlockEntity entity) {
        var block = entity.getCommandBlock();
        return offset(pos)
            + " success=" + block.getSuccessCount()
            + " powered=" + entity.isPowered()
            + " conditionMet=" + entity.wasConditionMet()
            + " auto=" + entity.isAutomatic()
            + " output=" + output(block.getLastOutput().getString());
    }

    private static String output(String text) {
        var untimed = Time.matcher(text).replaceFirst("");
        return untimed.isEmpty() ? "-" : untimed;
    }

    // What the operator's client shows of a chat packet, or null for any other packet.
    private static String chat(Packet<?> packet) {
        if (packet instanceof ClientboundSystemChatPacket system)
            return system.overlay() ? null : system.content().getString();

        if (packet instanceof ClientboundDisguisedChatPacket disguised)
            return disguised.chatType().decorate(disguised.message()).getString();

        if (packet instanceof ClientboundPlayerChatPacket player)
            return player.chatType().decorate(net.minecraft.network.chat.Component.literal(player.body().content()))
                .getString();

        return null;
    }

    // Each command block's saved fields after the last tick, sorted: "x,y,z Key=value".
    private static JsonArray saved(ServerLevel level, long start) {
        var saved = new JsonArray();
        for (var pos : compared()) {
            if (!(level.getBlockEntity(pos) instanceof CommandBlockEntity entity))
                continue;

            var tag = entity.saveCustomOnly(level.registryAccess());
            var fields = new ArrayList<String>();
            for (var key : new java.util.TreeSet<>(tag.keySet())) {
                var value = tag.get(key);
                String text;
                if (key.equals("LastOutput")) {
                    text = output(entity.getCommandBlock().getLastOutput().getString());
                } else if (key.equals("CustomName")) {
                    text = entity.getCommandBlock().getName().getString();
                } else if (key.equals("LastExecution") && value instanceof LongTag time) {
                    text = Long.toString(time.longValue() - start);
                } else if (value instanceof ByteTag flag) {
                    text = flag.byteValue() + "b";
                } else if (value instanceof IntTag number) {
                    text = Integer.toString(number.intValue());
                } else if (value instanceof StringTag string) {
                    text = string.value();
                } else {
                    text = String.valueOf(value);
                }

                fields.add(key + "=" + text);
            }

            saved.add(offset(pos) + " " + String.join(" ", fields));
        }

        return saved;
    }

    private static String offset(BlockPos pos) {
        var relative = pos.subtract(origin);
        return relative.getX() + "," + relative.getY() + "," + relative.getZ();
    }

    private static GameProfile profile(String name) {
        return new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
    }

    @SuppressWarnings("unchecked")
    private static List<ServerPlayer> players(PlayerList list) throws Exception {
        var field = PlayerList.class.getDeclaredField("players");
        field.setAccessible(true);
        return (List<ServerPlayer>) field.get(list);
    }

    // A game connection that keeps the packets sent to it instead of sending them, and handles the operator's edits.
    private static ServerGamePacketListenerImpl recorder(List<Packet<?>> packets, ServerPlayer player)
        throws Exception {
        var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
        var recorder = (Recorder) allocate.invoke(unsafe, Recorder.class);
        recorder.packets = packets;
        recorder.player = player;

        // The server ticks with this player in its list, which sends it chunks: it has none pending.
        var chunkSender = ServerGamePacketListenerImpl.class.getDeclaredField("chunkSender");
        var offset = (long) unsafe.getClass().getMethod("objectFieldOffset", java.lang.reflect.Field.class)
            .invoke(unsafe, chunkSender);
        unsafe.getClass().getMethod("putObject", Object.class, long.class, Object.class)
            .invoke(unsafe, recorder, offset, new net.minecraft.server.network.PlayerChunkSender(false));
        return recorder;
    }

    private static void clear(ServerLevel level) {
        var air = Blocks.AIR.defaultBlockState();
        for (var pos : BlockPos.betweenClosed(origin.offset(MinX - 1, MinY - 1, MinZ - 1),
            origin.offset(MaxX + 1, MaxY + 1, MaxZ + 1))) {
            level.setBlock(pos, air, QuietFlags);
        }
    }

    private static List<BlockState> snapshot(ServerLevel level) {
        var states = new ArrayList<BlockState>();
        for (var pos : compared())
            states.add(level.getBlockState(pos));

        return states;
    }

    private static String changes(ServerLevel level, List<BlockState> before) {
        var changed = new ArrayList<String>();
        int index = 0;
        for (var pos : compared()) {
            var state = level.getBlockState(pos);
            if (state != before.get(index))
                changed.add(offset(pos) + "=" + text(state));

            index++;
        }

        return String.join(";", changed);
    }

    // The compared area in a fixed order: y, then z, then x ascending.
    private static List<BlockPos> compared() {
        var positions = new ArrayList<BlockPos>();
        for (int y = MinY; y <= MaxY; y++) {
            for (int z = MinZ; z <= MaxZ; z++) {
                for (int x = MinX; x <= MaxX; x++)
                    positions.add(origin.offset(x, y, z));
            }
        }

        return positions;
    }

    // The state's block and the properties it doesn't share with the block's default state.
    private static String text(BlockState state) {
        var name = BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString();
        var defaults = state.getBlock().defaultBlockState();
        var properties = new ArrayList<String>();
        for (Property<?> property : state.getProperties()) {
            if (!state.getValue(property).equals(defaults.getValue(property)))
                properties.add(property.getName() + "=" + value(state, property));
        }

        return properties.isEmpty() ? name : name + "[" + String.join(",", properties) + "]";
    }

    private static <T extends Comparable<T>> String value(BlockState state, Property<T> property) {
        return property.getName(state.getValue(property));
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

    // Keeps each short array of numbers on one line.
    private static String compact(String json) {
        var numbers = Pattern.compile("\\[\\s*(-?[\\d.]+(?:,\\s*-?[\\d.]+)*)\\s*\\]");
        var matcher = numbers.matcher(json.replace("\r\n", "\n"));
        var result = new StringBuilder();
        while (matcher.find()) {
            var inner = matcher.group(1).replaceAll("\\s+", "").replace(",", ", ");
            matcher.appendReplacement(result, java.util.regex.Matcher.quoteReplacement("[" + inner + "]"));
        }

        matcher.appendTail(result);
        return result.toString();
    }

    private static void startServer() throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");

        // A flat world of air in plains, with nothing spawning, no random ticks and no watchdog.
        var properties = String.join("\n",
            "level-type=minecraft\\:flat",
            "generator-settings={\"layers\"\\:[{\"block\"\\:\"minecraft\\:air\",\"height\"\\:1}],"
                + "\"biome\"\\:\"minecraft\\:plains\"}",
            "generate-structures=false",
            "spawn-protection=0",
            "spawn-monsters=false",
            "spawn-animals=false",
            "difficulty=normal",
            "gamemode=survival",
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

        for (var command : List.of("gamerule random_tick_speed 0", "gamerule spawn_mobs false")) {
            server.submit(() -> server.getCommands().performPrefixedCommand(
                server.createCommandSourceStack(), command)).join();
        }
    }

    /** A connection allocated without its constructor, which keeps what is sent to it. */
    private static final class Recorder extends ServerGamePacketListenerImpl {
        List<Packet<?>> packets;

        private Recorder() {
            super(null, null, null, null);
            throw new UnsupportedOperationException();
        }

        @Override public void send(Packet<?> packet) {
            packets.add(packet);
        }

        @Override public void send(Packet<?> packet, ChannelFutureListener listener) {
            packets.add(packet);
        }

        // The server suspends and resumes each player's flushing around its tick; there's nothing to flush.
        @Override public void suspendFlushing() {
        }

        @Override public void resumeFlushing() {
        }
    }
}
