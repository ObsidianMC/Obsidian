import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonNull;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.item.BlockItem;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.SignBlockEntity;
import net.minecraft.world.level.block.entity.SignTextSlot;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;

/**
 * Records what every vanilla block item places when a survival player uses it on a block, through the same routine
 * the server runs for a use-item-on packet ({@code ServerPlayerGameMode.useItemOn}), on a headless dedicated server
 * in a flat world of air.
 *
 * <p>The fixture's {@code contexts} are its input: each names the blocks set up around the target position, the
 * block and face clicked, where on that face, and the player's rotation and sneaking. For every block item, in
 * registry order, the probe runs each context on a cleared area and records every block in the area that changed,
 * as {@code dx,dy,dz=state} relative to the target, joined with {@code ;}. A state lists only the properties that
 * differ from its block's default. An empty string means nothing changed. A context with {@code items} runs only for
 * those items, and records {@code null} for the others.
 */
public final class PlacementFixtures {
    // How far from the target the area that is cleared before and compared after each use reaches.
    private static final int Reach = 2;

    // Set without neighbor or shape updates and without drops, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    private static MinecraftServer server;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();

        try {
            startServer();
            if (args.length > 1 && args[1].equals("--observers")) {
                server.submit(() -> captureObservers(root, java.util.Set.of(args[2].split("\\+")))).join();
            } else {
                var items = new JsonObject();
                server.submit(() -> capture(root, items)).join();
                root.add("items", items);
            }
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }

        root.addProperty("version", server.getServerVersion());
        var gson = new GsonBuilder().serializeNulls().setPrettyPrinting().disableHtmlEscaping().create();
        var json = gson.toJson(root);
        Files.writeString(fixturePath, args.length > 1 ? json + "\n" : compact(json) + "\n",
            StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    // Use a real level here: the bounded construction probe deliberately skips setBlock's shape and onPlace work.
    private static void captureObservers(JsonObject root, java.util.Set<String> selected) {
        var level = server.overworld();
        var profile = new GameProfile(UUID.nameUUIDFromBytes("ObserverPlacer".getBytes(StandardCharsets.UTF_8)),
            "ObserverPlacer");
        var player = new ProbePlayer(server, level, profile);
        int captured = 0;
        for (var entry : root.getAsJsonArray("cases")) {
            var probe = entry.getAsJsonObject();
            if (!selected.contains(probe.get("name").getAsString())) {
                continue;
            }

            var target = position(probe.getAsJsonArray("origin"));
            for (int x = -1; x <= 1; x++) {
                for (int z = -1; z <= 1; z++) {
                    level.getChunk((target.getX() >> 4) + x, (target.getZ() >> 4) + z);
                }
            }

            clear(level, target);
            for (var value : probe.getAsJsonArray("setup")) {
                var placement = value.getAsJsonObject();
                level.setBlock(position(placement.getAsJsonArray("pos")),
                    state(placement.get("state").getAsString()), QuietFlags);
            }

            var stack = new ItemStack(net.minecraft.world.item.Items.OBSERVER, 2);
            use(level, player, probe.getAsJsonArray("player"), target, probe, stack);
            probe.addProperty("placed", text(level.getBlockState(target)));
            probe.addProperty("scheduled", level.getBlockTicks().hasScheduledTick(target, Blocks.OBSERVER));
            probe.addProperty("remaining", stack.getCount());
            captured++;
        }

        if (captured != selected.size()) {
            throw new IllegalArgumentException("Every selected observer placement must exist once.");
        }
    }

    private static void capture(JsonObject root, JsonObject items) {
        var level = server.overworld();
        var target = position(root.getAsJsonArray("target"));
        var standing = root.getAsJsonArray("player");
        var contexts = root.getAsJsonArray("contexts");

        for (int x = (target.getX() - Reach - 1) >> 4; x <= (target.getX() + Reach + 1) >> 4; x++) {
            for (int z = (target.getZ() - Reach - 1) >> 4; z <= (target.getZ() + Reach + 1) >> 4; z++)
                level.getChunk(x, z);
        }

        var profile = new GameProfile(UUID.nameUUIDFromBytes("Placer".getBytes(StandardCharsets.UTF_8)), "Placer");
        var player = new ProbePlayer(server, level, profile);

        int count = 0;
        for (var item : BuiltInRegistries.ITEM) {
            if (!(item instanceof BlockItem) || !item.isEnabled(level.enabledFeatures()))
                continue;

            var key = BuiltInRegistries.ITEM.getKey(item).toString();
            var results = new JsonArray();
            for (var element : contexts) {
                var context = element.getAsJsonObject();
                if (!appliesTo(context, key)) {
                    results.add(JsonNull.INSTANCE);
                    continue;
                }

                clear(level, target);
                for (var setup : context.getAsJsonArray("setup")) {
                    var placement = setup.getAsJsonObject();
                    var pos = target.offset(position(placement.getAsJsonArray("offset")));
                    level.setBlock(pos, state(placement.get("state").getAsString()), QuietFlags);
                }

                var before = snapshot(level, target);
                use(level, player, standing, target, context, new ItemStack(item, 2));
                results.add(changes(level, target, before));
            }

            items.add(key, results);
            count++;
        }

        clear(level, target);
        System.out.println("Placed " + count + " block items in " + contexts.size() + " contexts");
    }

    // Whether a context is run for an item: every item, or only those its "items" name.
    private static boolean appliesTo(JsonObject context, String item) {
        if (!context.has("items"))
            return true;

        for (var name : context.getAsJsonArray("items")) {
            if (name.getAsString().equals(item))
                return true;
        }

        return false;
    }

    // Uses the item on the context's clicked face, as the server does for a use-item-on packet.
    private static void use(ServerLevel level, ProbePlayer player, JsonArray standing, BlockPos target,
        JsonObject context, ItemStack stack) {
        player.snapTo(
            standing.get(0).getAsDouble(),
            standing.get(1).getAsDouble(),
            standing.get(2).getAsDouble(),
            context.get("yaw").getAsFloat(),
            context.get("pitch").getAsFloat());
        // A player's head turns with them every tick (Player.aiStep), and placement reads the head's rotation.
        player.setYHeadRot(player.getYRot());
        player.setShiftKeyDown(context.get("sneaking").getAsBoolean());
        player.setItemInHand(InteractionHand.MAIN_HAND, stack);

        var clicked = target.offset(position(context.getAsJsonArray("clicked")));
        var cursor = context.getAsJsonArray("cursor");
        var location = new Vec3(
            clicked.getX() + cursor.get(0).getAsDouble(),
            clicked.getY() + cursor.get(1).getAsDouble(),
            clicked.getZ() + cursor.get(2).getAsDouble());
        var face = Direction.valueOf(context.get("face").getAsString().toUpperCase(Locale.ROOT));
        var hit = new BlockHitResult(location, face, clicked, false);

        player.gameMode.useItemOn(player, level, stack, InteractionHand.MAIN_HAND, hit);
    }

    private static void clear(ServerLevel level, BlockPos target) {
        var air = Blocks.AIR.defaultBlockState();
        for (var pos : BlockPos.betweenClosed(target.offset(-Reach - 1, -Reach - 1, -Reach - 1),
            target.offset(Reach + 1, Reach + 1, Reach + 1))) {
            level.setBlock(pos, air, QuietFlags);
        }
    }

    private static List<BlockState> snapshot(ServerLevel level, BlockPos target) {
        var states = new ArrayList<BlockState>();
        for (var pos : area(target))
            states.add(level.getBlockState(pos));

        return states;
    }

    private static String changes(ServerLevel level, BlockPos target, List<BlockState> before) {
        var changed = new ArrayList<String>();
        int index = 0;
        for (var pos : area(target)) {
            var state = level.getBlockState(pos);
            if (state != before.get(index)) {
                var offset = pos.subtract(target);
                changed.add(offset.getX() + "," + offset.getY() + "," + offset.getZ() + "=" + text(state));
            }

            index++;
        }

        return String.join(";", changed);
    }

    // The compared area in a fixed order: y, then z, then x ascending.
    private static List<BlockPos> area(BlockPos target) {
        var positions = new ArrayList<BlockPos>();
        for (int y = -Reach; y <= Reach; y++) {
            for (int z = -Reach; z <= Reach; z++) {
                for (int x = -Reach; x <= Reach; x++)
                    positions.add(target.offset(x, y, z));
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

    // Keeps each item's results, and each short array of numbers, on one line.
    private static String compact(String json) {
        var numbers = java.util.regex.Pattern.compile("\\[\\s*(-?[\\d.]+(?:,\\s*-?[\\d.]+)*)\\s*\\]");
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

        // A flat world of air in plains, with nothing spawning and no watchdog, since the whole capture runs as
        // one task on the server thread.
        var properties = String.join("\n",
            "level-type=minecraft\\:flat",
            "generator-settings={\"layers\"\\:[{\"block\"\\:\"minecraft\\:air\",\"height\"\\:1}],"
                + "\"biome\"\\:\"minecraft\\:plains\"}",
            "generate-structures=false",
            "spawn-protection=0",
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
    }

    /** A player without a connection: placing a sign would open its editor on the client, which there isn't. */
    private static final class ProbePlayer extends ServerPlayer {
        ProbePlayer(MinecraftServer server, ServerLevel level, GameProfile profile) {
            super(server, level, profile, ClientInformation.createDefault());
        }

        @Override public void openTextEdit(SignBlockEntity sign, SignTextSlot slot) {
        }
    }
}
