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
import net.minecraft.network.protocol.Packet;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.players.NameAndId;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;

import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.IntArrayTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.StringTag;
import net.minecraft.nbt.Tag;
import net.minecraft.nbt.TagParser;
import net.minecraft.network.FriendlyByteBuf;
import io.netty.buffer.Unpooled;

import net.minecraft.core.Direction;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.item.context.BlockPlaceContext;
import net.minecraft.world.item.context.UseOnContext;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.level.block.entity.JigsawBlockEntity;
import net.minecraft.network.protocol.game.ServerboundSetJigsawBlockPacket;
import net.minecraft.network.protocol.game.ServerboundJigsawGeneratePacket;

/** Records jigsaw editor decoding, saves, updates, placement and generation on a real 26.3 server. */
public final class JigsawBlockFixtures {
    private static MinecraftServer server;
    private static BlockPos origin;
    private static final int QuietFlags = 2 | 16 | 32 | 256 | 512;

    public static void main(String[] args) throws Exception {
        var path = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        var selected = args.length > 1 && !args[1].isBlank() ? Set.of(args[1].split("\\+")) : Set.<String>of();
        try {
            installPools(root);
            startServer();
            origin = position(root.getAsJsonArray("origin"));
            server.submit(() -> server.tickRateManager().setFrozen(true)).join();
            var player = new ServerPlayer(server, server.overworld(), profile("Operator"),
                ClientInformation.createDefault());
            var heard = new ArrayList<Packet<?>>();
            player.connection = recorder(heard, player);
            server.submit(() -> {
                player.setGameMode(GameType.CREATIVE);
                server.getPlayerList().op(new NameAndId(player.getGameProfile()));
                try {
                    installTemplates(root);
                } catch (Exception error) {
                    throw new RuntimeException(error);
                }
            }).join();
            int count = 0;
            for (var value : root.getAsJsonArray("cases")) {
                var input = value.getAsJsonObject();
                if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                    continue;

                server.submit(() -> capture(input, player, heard)).join();
                count++;
            }

            if (!selected.isEmpty() && count != selected.size())
                throw new IllegalArgumentException("Every selected case must exist and be named once");

            root.addProperty("version", server.getServerVersion());
            root.addProperty("seed", server.overworld().getSeed());
            var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().serializeNulls().create();
            Files.writeString(path, (compact(gson.toJson(root)) + "\n").replace("\n", "\r\n"),
                StandardCharsets.UTF_8);
            System.out.println("Ran " + count + " jigsaw block cases");
            System.out.flush();
            Runtime.getRuntime().halt(0);
        } catch (Throwable error) {
            error.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }
    }

    private static void installPools(JsonObject root) throws Exception {
        var pack = Path.of("world", "datapacks", "jigsaw-fixtures");
        Files.createDirectories(pack);
        Files.writeString(pack.resolve("pack.mcmeta"),
            "{\"pack\":{\"description\":\"Jigsaw capture inputs\",\"min_format\":121,\"max_format\":121}}");
        for (var entry : root.getAsJsonObject("pools").entrySet()) {
            var id = Identifier.parse(entry.getKey());
            var path = pack.resolve("data").resolve(id.getNamespace()).resolve("worldgen/template_pool")
                .resolve(id.getPath() + ".json");
            Files.createDirectories(path.getParent());
            Files.writeString(path, entry.getValue().toString());
        }
    }

    private static void installTemplates(JsonObject root) throws Exception {
        var level = server.overworld();
        var source = origin.offset(256, 0, 256);
        for (var entry : root.getAsJsonObject("templates").entrySet()) {
            var template = entry.getValue().getAsJsonObject();
            var size = position(template.getAsJsonArray("size"));
            for (var pos : BlockPos.betweenClosed(source, source.offset(size).offset(-1, -1, -1)))
                level.setBlock(pos, Blocks.AIR.defaultBlockState(), QuietFlags);

            for (var block : template.getAsJsonArray("blocks"))
                place(level, source, block.getAsJsonObject());

            level.getStructureTemplateManager().getOrCreate(Identifier.parse(entry.getKey()))
                .fillFromWorld(level, source, size, false,
                    template.has("ignoreAir") ? List.of(Blocks.AIR) : List.of());
        }
    }

    private static void place(ServerLevel level, BlockPos base, JsonObject block) throws Exception {
        var pos = base.offset(position(block.getAsJsonArray("offset")));
        level.setBlock(pos, state(block.get("state").getAsString()), QuietFlags);
        if (block.has("nbt")) {
            var tag = TagParser.parseCompoundFully(block.get("nbt").getAsString());
            level.getBlockEntity(pos).loadWithComponents(net.minecraft.world.level.storage.TagValueInput.create(
                net.minecraft.util.ProblemReporter.DISCARDING, level.registryAccess(), tag));
        }
    }

    private static void capture(JsonObject input, ServerPlayer player, List<Packet<?>> heard) {
        var level = server.overworld();
        try {
            var min = position(input.getAsJsonArray("min"));
            var max = position(input.getAsJsonArray("max"));
            for (var pos : BlockPos.betweenClosed(origin.offset(min), origin.offset(max)))
                level.setBlock(pos, Blocks.AIR.defaultBlockState(), QuietFlags);

            for (var value : input.getAsJsonArray("setup"))
                place(level, origin, value.getAsJsonObject());

            var checkpoints = new JsonArray();
            for (var value : input.getAsJsonArray("actions")) {
                var action = value.getAsJsonObject();
                player.setGameMode(GameType.byName(action.has("mode") ? action.get("mode").getAsString() : "creative"));
                var permission = action.has("permission") ? action.get("permission").getAsInt() : 4;
                server.getPlayerList().getOps().add(new net.minecraft.server.players.ServerOpListEntry(
                    new NameAndId(player.getGameProfile()),
                    net.minecraft.server.permissions.LevelBasedPermissionSet.forLevel(
                        net.minecraft.server.permissions.PermissionLevel.byId(permission)), false));
                var pos = origin.offset(action.has("offset") ? position(action.getAsJsonArray("offset")) : BlockPos.ZERO);
                heard.clear();
                var observation = new JsonObject();
                if (action.has("edit")) {
                    edit(player, pos, action.getAsJsonObject("edit"));
                } else if (action.has("generate")) {
                    var generate = action.getAsJsonObject("generate");
                    var buffer = new FriendlyByteBuf(Unpooled.buffer());
                    buffer.writeBlockPos(pos);
                    buffer.writeVarInt(generate.get("levels").getAsInt());
                    buffer.writeBoolean(generate.get("keepJigsaws").getAsBoolean());
                    var packet = ServerboundJigsawGeneratePacket.STREAM_CODEC.decode(buffer);
                    buffer.release();
                    observation.addProperty("levels", packet.levels());
                    var chunks = new java.util.TreeSet<String>();
                    if (input.has("recordChunks")) {
                        System.getProperties().put("obsidian.jigsaw.chunks", (java.util.function.Consumer<Object>) raw -> {
                            var access = (Object[]) raw;
                            if (access[2] == net.minecraft.world.level.chunk.status.ChunkStatus.FULL
                                && Boolean.TRUE.equals(access[3])) {
                                chunks.add(access[0] + "," + access[1]);
                            }
                        });
                    }
                    try {
                        player.connection.handleJigsawGenerate(packet);
                        observation.addProperty("error", false);
                    } catch (IllegalStateException error) {
                        observation.addProperty("error", true);
                    } finally {
                        System.getProperties().remove("obsidian.jigsaw.chunks");
                    }
                    if (input.has("recordChunks")) {
                        var requests = new JsonArray();
                        for (var chunk : chunks)
                            requests.add(chunk);

                        observation.add("chunks", requests);
                    }
                } else if (action.has("reload")) {
                    var entity = level.getBlockEntity(pos);
                    var tag = entity.saveWithoutMetadata(level.registryAccess());
                    var block = level.getBlockState(pos);
                    level.setBlock(pos, Blocks.AIR.defaultBlockState(), QuietFlags);
                    level.setBlock(pos, block, QuietFlags);
                    level.getBlockEntity(pos).loadWithComponents(net.minecraft.world.level.storage.TagValueInput.create(
                        net.minecraft.util.ProblemReporter.DISCARDING, level.registryAccess(), tag));
                } else if (action.has("use")) {
                    var result = level.getBlockState(pos).useWithoutItem(level, player,
                        new BlockHitResult(Vec3.atCenterOf(pos), Direction.UP, pos, false));
                    observation.addProperty("consumes", result.consumesAction());
                } else if (action.has("placement")) {
                    var placement = action.getAsJsonObject("placement");
                    player.setYRot(placement.get("yaw").getAsFloat());
                    var face = Direction.valueOf(placement.get("face").getAsString().toUpperCase(Locale.ROOT));
                    var context = new BlockPlaceContext(new UseOnContext(player, InteractionHand.MAIN_HAND,
                        new BlockHitResult(Vec3.atCenterOf(pos), face, pos, false)));
                    observation.addProperty("placement", text(Blocks.JIGSAW.getStateForPlacement(context)));
                }

                observation.add("world", checkpoint(level, min, max));
                checkpoints.add(observation);
            }

            input.add("checkpoints", checkpoints);
        } catch (Exception error) {
            throw new RuntimeException(input.get("name").getAsString(), error);
        }
    }

    private static void edit(ServerPlayer player, BlockPos pos, JsonObject edit) {
        var buffer = new FriendlyByteBuf(Unpooled.buffer());
        buffer.writeBlockPos(pos);
        buffer.writeUtf(edit.get("name").getAsString());
        buffer.writeUtf(edit.get("target").getAsString());
        buffer.writeUtf(edit.get("pool").getAsString());
        buffer.writeUtf(edit.get("finalState").getAsString());
        buffer.writeUtf(edit.get("joint").getAsString());
        buffer.writeVarInt(edit.get("selectionPriority").getAsInt());
        buffer.writeVarInt(edit.get("placementPriority").getAsInt());
        var packet = ServerboundSetJigsawBlockPacket.STREAM_CODEC.decode(buffer);
        buffer.release();
        player.connection.handleSetJigsawBlock(packet);
    }

    private static JsonObject checkpoint(ServerLevel level, BlockPos min, BlockPos max) {
        var result = new JsonObject();
        var blocks = new JsonArray();
        var saved = new JsonObject();
        var updates = new JsonObject();
        for (var pos : BlockPos.betweenClosed(origin.offset(min), origin.offset(max))) {
            var block = level.getBlockState(pos);
            var offset = pos.subtract(origin);
            var key = offset.getX() + "," + offset.getY() + "," + offset.getZ();
            if (!block.isAir())
                blocks.add(key + "=" + text(block));

            if (level.getBlockEntity(pos) instanceof JigsawBlockEntity entity) {
                saved.add(key, canonical(entity.saveWithoutMetadata(level.registryAccess())));
                updates.add(key, canonical(entity.getUpdateTag(level.registryAccess())));
            }
        }

        result.add("blocks", blocks);
        result.add("saved", saved);
        result.add("updates", updates);
        return result;
    }

    private static com.google.gson.JsonElement canonical(Tag tag) {
        if (tag instanceof CompoundTag compound) {
            var result = new JsonObject();
            for (var key : new java.util.TreeSet<>(compound.keySet()))
                result.add(key, canonical(compound.get(key)));

            return result;
        }

        if (tag instanceof ListTag list) {
            var result = new JsonArray();
            for (var item : list)
                result.add(canonical(item));

            return result;
        }

        if (tag instanceof IntArrayTag array) {
            var result = new JsonArray();
            for (int number : array.getAsIntArray())
                result.add("3:" + number);

            return result;
        }

        if (tag instanceof StringTag string)
            return new com.google.gson.JsonPrimitive(string.value());

        return new com.google.gson.JsonPrimitive(tag.getId() + ":" + tag.toString().toLowerCase(Locale.ROOT)
            .replaceAll("\\.0([fd]?)$", "$1"));
    }

    private static GameProfile profile(String name) {
        return new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
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
            "level-seed=12345",
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

        server.submit(() -> {}).join();

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
