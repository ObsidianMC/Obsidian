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
import net.minecraft.network.protocol.game.ClientboundSystemChatPacket;
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

import net.minecraft.world.level.block.entity.StructureBlockEntity;
import net.minecraft.world.level.block.state.properties.StructureMode;
import net.minecraft.network.protocol.game.ServerboundSetStructureBlockPacket;
import net.minecraft.world.level.block.Mirror;
import net.minecraft.world.level.block.Rotation;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.IntArrayTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.NbtAccounter;
import net.minecraft.nbt.NbtIo;
import net.minecraft.nbt.StringTag;
import net.minecraft.nbt.Tag;
import net.minecraft.nbt.TagParser;
import net.minecraft.network.FriendlyByteBuf;
import io.netty.buffer.Unpooled;

/** Captures structure editor packets, saved data, generated files and redstone on the real 26.3 server. */
public final class StructureBlockFixtures {
    private static MinecraftServer server;
    private static BlockPos origin;
    private static final int QuietFlags = 2 | 16 | 32 | 256 | 512;

    public static void main(String[] args) throws Exception {
        var path = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        var selected = args.length > 1 && !args[1].isBlank() ? Set.of(args[1].split("\\+")) : Set.<String>of();
        try {
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
            }).join();
            int count = 0;
            for (var element : root.getAsJsonArray("cases")) {
                var input = element.getAsJsonObject();
                if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                    continue;

                server.submit(() -> capture(input, player, heard)).join();
                count++;
            }

            if (!selected.isEmpty() && count != selected.size())
                throw new IllegalArgumentException("Every selected case must exist and be named once");

            root.addProperty("version", server.getServerVersion());
            var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().serializeNulls().create();
            Files.writeString(path, compact(gson.toJson(root)) + "\n", StandardCharsets.UTF_8);
            System.out.println("Ran " + count + " structure block cases");
            System.out.flush();
            Runtime.getRuntime().halt(0);
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }
    }

    private static void capture(JsonObject input, ServerPlayer player, List<Packet<?>> heard) {
        var level = server.overworld();
        try {
            for (var pos : BlockPos.betweenClosed(origin.offset(-6, -1, -6), origin.offset(8, 5, 8)))
                level.setBlock(pos, Blocks.AIR.defaultBlockState(), QuietFlags);

            // Every case owns its template id; a redstone-only save must never create a disk file.
            for (var name : List.of("test:roundtrip", "test:redstone", "test:empty", "test:strict"))
                level.getStructureTemplateManager().remove(Identifier.parse(name));

            var generated = Path.of("world", "generated");
            if (Files.exists(generated)) {
                try (var files = Files.walk(generated)) {
                    for (var file : files.filter(Files::isRegularFile).toList())
                        Files.delete(file);
                }
            }

            player.setGameMode(GameType.CREATIVE);
            for (var value : input.getAsJsonArray("setup")) {
                var placement = value.getAsJsonObject();
                var pos = origin.offset(position(placement.getAsJsonArray("offset")));
                level.setBlock(pos, state(placement.get("state").getAsString()), QuietFlags);
                if (placement.has("nbt")) {
                    var tag = TagParser.parseCompoundFully(placement.get("nbt").getAsString());
                    var data = net.minecraft.world.level.storage.TagValueInput.create(
                        net.minecraft.util.ProblemReporter.DISCARDING, level.registryAccess(), tag);
                    level.getBlockEntity(pos).loadWithComponents(data);
                }

                if (placement.has("author"))
                    ((StructureBlockEntity) level.getBlockEntity(pos)).createdBy(player);
            }

            var setupEntities = new JsonArray();
            if (input.has("entities")) {
                for (var entityInput : input.getAsJsonArray("entities")) {
                    var definition = entityInput.getAsJsonObject();
                    var at = position(definition.getAsJsonArray("offset"));
                    var stand = new net.minecraft.world.entity.decoration.ArmorStand(level,
                        origin.getX() + at.getX() + 0.5, origin.getY() + at.getY(),
                        origin.getZ() + at.getZ() + 0.5);
                    stand.setUUID(new UUID(0, setupEntities.size() + 1));
                    stand.setNoGravity(true);
                    stand.setYRot(30);
                    level.addFreshEntity(stand);
                    var output = net.minecraft.world.level.storage.TagValueOutput.createWithContext(
                        net.minecraft.util.ProblemReporter.DISCARDING, level.registryAccess());
                    stand.save(output);
                    setupEntities.add(output.buildResult().toString());
                }
            }

            if (input.has("entities"))
                input.add("entitySetup", setupEntities);

            heard.clear();
            var checkpoints = new JsonArray();
            for (var value : input.getAsJsonArray("actions")) {
                var action = value.getAsJsonObject();
                if (action.has("edit")) {
                    var edit = action.getAsJsonObject("edit");
                    player.setGameMode(action.has("survival") ? GameType.SURVIVAL : GameType.CREATIVE);
                    boolean op = !action.has("nonOp");
                    if (!op)
                        server.getPlayerList().deop(new NameAndId(player.getGameProfile()));

                    edit(player, edit);
                    if (!op)
                        server.getPlayerList().op(new NameAndId(player.getGameProfile()));
                } else {
                    var pos = origin.offset(position(action.getAsJsonArray("offset")));
                    level.setBlock(pos, state(action.get("state").getAsString()),
                        action.has("quiet") ? QuietFlags : Block.UPDATE_ALL);
                }

                checkpoints.add(checkpoint(level, heard, generated));
                heard.clear();
            }

            input.add("checkpoints", checkpoints);
            // Corner range cases deliberately put blocks outside the compared box.
            for (var value : input.getAsJsonArray("setup")) {
                var placement = value.getAsJsonObject();
                var pos = origin.offset(position(placement.getAsJsonArray("offset")));
                level.setBlock(pos, Blocks.AIR.defaultBlockState(), QuietFlags);
            }

            for (var entity : level.getAllEntities()) {
                if (!(entity instanceof ServerPlayer))
                    entity.discard();
            }
        } catch (Exception e) {
            throw new RuntimeException(input.get("name").getAsString(), e);
        }
    }

    private static void edit(ServerPlayer player, JsonObject edit) {
        var offset = position(edit.getAsJsonArray("pos"));
        var size = position(edit.getAsJsonArray("size"));
        var packet = new ServerboundSetStructureBlockPacket(
            origin.offset(position(edit.getAsJsonArray("offset"))),
            StructureBlockEntity.UpdateType.values()[edit.get("action").getAsInt()],
            StructureMode.values()[edit.get("mode").getAsInt()], edit.get("name").getAsString(),
            offset, size, Mirror.values()[edit.get("mirror").getAsInt()],
            Rotation.values()[edit.get("rotation").getAsInt()], edit.get("metadata").getAsString(),
            edit.get("ignoreEntities").getAsBoolean(), edit.get("strict").getAsBoolean(),
            edit.get("showAir").getAsBoolean(), edit.get("showBox").getAsBoolean(),
            edit.get("integrity").getAsFloat(), edit.get("seed").getAsLong());

        // Decode the wire bytes too: constructor setters don't apply the decoder's signed-byte clamps.
        var buffer = new FriendlyByteBuf(Unpooled.buffer());
        ServerboundSetStructureBlockPacket.STREAM_CODEC.encode(buffer, packet);
        var decoded = ServerboundSetStructureBlockPacket.STREAM_CODEC.decode(buffer);
        buffer.release();
        player.connection.handleSetStructureBlock(decoded);
    }

    private static JsonObject checkpoint(ServerLevel level, List<Packet<?>> heard, Path generated) throws Exception {
        var result = new JsonObject();
        var blocks = new JsonArray();
        var saved = new JsonObject();
        var blockEntities = new JsonObject();
        for (int y = -1; y <= 5; y++) {
            for (int z = -6; z <= 8; z++) {
                for (int x = -6; x <= 8; x++) {
                    var pos = origin.offset(x, y, z);
                    var state = level.getBlockState(pos);
                    var key = x + "," + y + "," + z;
                    if (!state.isAir())
                        blocks.add(key + "=" + text(state));

                    if (level.getBlockEntity(pos) instanceof StructureBlockEntity entity) {
                        saved.add(key, canonical(entity.saveCustomOnly(level.registryAccess())));
                    } else if (level.getBlockEntity(pos)
                        instanceof net.minecraft.world.level.block.entity.BlockEntity entity) {
                        var output = net.minecraft.world.level.storage.TagValueOutput.createWithContext(
                            net.minecraft.util.ProblemReporter.DISCARDING, level.registryAccess());
                        entity.saveWithId(output);
                        blockEntities.add(key, canonical(output.buildResult()));
                    }
                }
            }
        }

        result.add("blocks", blocks);
        result.add("saved", saved);
        result.add("blockEntities", blockEntities);
        var chat = new JsonArray();
        for (var packet : heard) {
            if (packet instanceof ClientboundSystemChatPacket system && !system.overlay())
                chat.add(system.content().getString());
        }

        result.add("chat", chat);
        var files = new JsonObject();
        if (Files.exists(generated)) {
            try (var paths = Files.walk(generated)) {
                for (var path : paths.filter(Files::isRegularFile).sorted().toList()) {
                    var tag = NbtIo.readCompressed(path, NbtAccounter.unlimitedHeap());
                    files.add(generated.relativize(path).toString().replace('\\', '/'), canonical(tag));
                }
            }
        }

        result.add("files", files);
        var entities = new ArrayList<String>();
        var itemCounts = new TreeMap<String, Integer>();
        for (var entity : level.getAllEntities()) {
            if (entity instanceof net.minecraft.world.entity.item.ItemEntity item) {
                var id = BuiltInRegistries.ITEM.getKey(item.getItem().getItem()).toString();
                itemCounts.merge(id, item.getItem().getCount(), Integer::sum);
            }

            if (!(entity instanceof net.minecraft.world.entity.decoration.ArmorStand))
                continue;

            entities.add(BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString() + " "
                + (entity.getX() - origin.getX()) + "," + (entity.getY() - origin.getY()) + ","
                + (entity.getZ() - origin.getZ()) + " yaw=" + entity.getYRot());
        }

        entities.sort(String::compareTo);
        var observedEntities = new JsonArray();
        for (var entity : entities)
            observedEntities.add(entity);

        result.add("entities", observedEntities);
        var items = new JsonObject();
        for (var entry : itemCounts.entrySet())
            items.addProperty(entry.getKey(), entry.getValue());

        result.add("items", items);
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
