import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import java.lang.reflect.Method;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.entity.HopperBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.gameevent.GameEventListener;

/**
 * Records what shelves do: a player's use through {@code ServerPlayerGameMode.useItemOn}, a block set beside them
 * with every update (which powers, unpowers and connects them), one hopper transfer into or out of them, and a
 * player breaking one.
 *
 * <p>The cases use {@code block_use.json}'s input format, and the probe runs on {@code BlockUseFixtures}' dedicated
 * server, players, packet recorders, game event listener and write observer, which it calls by reflection so that
 * probe stays as it is. A case's {@code kind} is {@code use} (the default), {@code set} (its {@code place} is set
 * with {@code Block.UPDATE_ALL}), {@code hopper} (the hopper at {@code hopper} runs
 * {@code HopperBlockEntity.pushItemsTick} once) or {@code break} (the user breaks the clicked block). A case may
 * select a hotbar slot ({@code selected}) and fill hotbar slots ({@code hotbar}: slot, item and count).
 *
 * <p>Every case records what a {@code block_use} case records with {@code checkWrites} and {@code checkEffects},
 * the sounds, level events and block events the acting player's own client was sent ({@code userEffects}), the
 * saved data of every block entity of its setup in order ({@code blockEntities}), and with {@code checkInventory}
 * the user's 36 storage slots. A {@code comparator} offset records that comparator's output once its pending tick ran.
 */
public final class ShelfFixtures {
    // Set without neighbor or shape updates and without drops, like BlockUseFixtures' setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();
        var selected = args.length > 1 && !args[1].isBlank() ? Set.of(args[1].split("\\+")) : Set.<String>of();

        MinecraftServer server = null;
        try {
            call("startServer");
            call("forceArea", root);
            server = (MinecraftServer) staticField("server");
            var running = server;
            server.submit(() -> {
                try {
                    capture(running, root, selected);
                } catch (Exception e) {
                    throw new RuntimeException(e);
                }
            }).join();
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }

        root.addProperty("version", server.getServerVersion());
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().serializeNulls().create();
        var json = (String) call("compact", gson.toJson(root));
        Files.writeString(fixturePath, json + "\n", StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    private static void capture(MinecraftServer server, JsonObject root, Set<String> selected) throws Exception {
        var level = server.overworld();
        var target = (BlockPos) call("position", root.getAsJsonArray("target"));
        var standing = root.getAsJsonArray("player");

        var userPackets = new ArrayList<Packet<?>>();
        var user = probePlayer(server, level, "User", userPackets);

        // A second player above the target hears what is broadcast around it.
        var heard = new ArrayList<Packet<?>>();
        var bystander = probePlayer(server, level, "Bystander", heard);
        bystander.snapTo(target.getX() + 0.5, target.getY() + 3, target.getZ() + 0.5, 0, 0);
        @SuppressWarnings("unchecked")
        var players = (List<ServerPlayer>) call("players", server.getPlayerList());
        for (var player : List.of(user, bystander)) {
            players.add(player);
            level.players().add(player);
        }

        var gameEvents = new ArrayList<String>();
        var listener = (GameEventListener) construct("Listener", target, user, gameEvents);
        var section = SectionPos.blockToSectionCoord(target.getY());
        level.getChunkAt(target).getListenerRegistry(section).register(listener);

        for (var element : root.getAsJsonArray("cases")) {
            var input = element.getAsJsonObject();
            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            setUp(level, target, input);
            user.getInventory().setSelectedSlot(input.has("selected") ? input.get("selected").getAsInt() : 0);
            call("prepare", user, standing, input);
            fillHotbar(user, input);

            var before = call("snapshot", level, target);
            userPackets.clear();
            heard.clear();
            gameEvents.clear();
            awarded(user).clear();

            var hand = (InteractionHand) call("hand", input);

            // The write observer reads its switch from the object it is given and records into it.
            var observed = new JsonObject();
            observed.addProperty("checkWrites", true);
            call("observeWrites", level, observed);

            var kind = input.has("kind") ? input.get("kind").getAsString() : "use";
            var result = act(level, user, target, input, kind);

            call("runBlockEvents", level);
            System.getProperties().remove("obsidian.blockUse.writes");
            System.getProperties().remove("obsidian.blockUse.destroyed");

            if (result != null)
                input.addProperty("result", (String) call("result", result));

            input.addProperty("changes", (String) call("changes", level, target, before));
            input.addProperty("hand", (String) call("stack", user.getItemInHand(hand)));
            if (input.has("checkInventory"))
                input.add("inventory", inventory(level, user));

            call("recordExtra", input, heard);
            input.add("entities", (JsonArray) call("detailedEntities", level, target));
            call("record", input, heard, gameEvents);
            input.add("stats", (JsonArray) call("strings", awarded(user)));
            input.add("userEffects", userEffects(userPackets));
            input.add("writes", observed.get("writes"));
            input.add("destroyed", observed.get("destroyed"));
            input.add("blockEntities", blockEntities(level, target, input));
            if (input.has("comparator"))
                call("recordHeldItems", level, user, target, input, heard, gameEvents);
        }

        call("clear", level, target);
        for (var player : List.of(user, bystander)) {
            players.remove(player);
            level.players().remove(player);
        }
        System.out.println("Recorded " + root.getAsJsonArray("cases").size() + " shelf cases");
    }

    // What the case does: a use, a block set with every update, a hopper's transfer or the user breaking the block.
    private static InteractionResult act(ServerLevel level, ServerPlayer user, BlockPos target, JsonObject input,
        String kind) throws Exception {
        switch (kind) {
            case "use" -> {
                return (InteractionResult) call("use", level, user, target, input);
            }
            case "set" -> {
                var place = input.getAsJsonObject("place");
                var pos = target.offset((BlockPos) call("position", place.getAsJsonArray("offset")));
                var state = (BlockState) call("state", place.get("state").getAsString());
                level.setBlock(pos, state, Block.UPDATE_ALL);
                return null;
            }
            case "hopper" -> {
                var pos = target.offset((BlockPos) call("position", input.getAsJsonArray("hopper")));
                var hopper = (HopperBlockEntity) level.getBlockEntity(pos);
                HopperBlockEntity.pushItemsTick(level, pos, level.getBlockState(pos), hopper);
                return null;
            }
            case "break" -> {
                var clicked = target.offset((BlockPos) call("position", input.getAsJsonArray("clicked")));
                user.gameMode.destroyBlock(clicked);
                return null;
            }
            default -> throw new IllegalArgumentException("Unknown kind " + kind);
        }
    }

    // The case's blocks in a cleared area, set without updates, with their block entity data.
    private static void setUp(ServerLevel level, BlockPos target, JsonObject input) throws Exception {
        call("clear", level, target);
        call("discardEntities", level, target);
        for (var setup : input.getAsJsonArray("setup")) {
            var placement = setup.getAsJsonObject();
            var pos = target.offset((BlockPos) call("position", placement.getAsJsonArray("offset")));
            var state = (BlockState) call("state", placement.get("state").getAsString());
            level.setBlock(pos, state, QuietFlags);
            if (placement.has("nbt"))
                call("loadData", level, pos, placement.get("nbt").getAsString());
        }
    }

    // The hotbar slots the case fills, after the hands are set.
    private static void fillHotbar(ServerPlayer user, JsonObject input) {
        if (!input.has("hotbar"))
            return;

        for (var element : input.getAsJsonArray("hotbar")) {
            var entry = element.getAsJsonArray();
            var item = BuiltInRegistries.ITEM.getValue(Identifier.parse(entry.get(1).getAsString()));
            user.getInventory().setItem(entry.get(0).getAsInt(), new ItemStack(item, entry.get(2).getAsInt()));
        }
    }

    // The user's 36 storage slots in vanilla's order, with the components each stack changes.
    private static JsonArray inventory(ServerLevel level, ServerPlayer user) throws Exception {
        var slots = new JsonArray();
        for (int slot = 0; slot < 36; slot++)
            slots.add((String) call("detailedStack", level, user.getInventory().getItem(slot)));

        return slots;
    }

    // The saved data of every block entity the setup placed, in its order, as "x,y,z <snbt>" or "x,y,z none".
    private static JsonArray blockEntities(ServerLevel level, BlockPos target, JsonObject input) throws Exception {
        var saved = new JsonArray();
        for (var setup : input.getAsJsonArray("setup")) {
            var offset = (BlockPos) call("position", setup.getAsJsonObject().getAsJsonArray("offset"));
            var pos = target.offset(offset);
            if (!level.getBlockState(pos).hasBlockEntity() && level.getBlockEntity(pos) == null)
                continue;

            var data = (JsonElement) call("savedData", level, pos);
            var text = data.isJsonNull() ? "none" : data.getAsString();
            saved.add(offset.getX() + "," + offset.getY() + "," + offset.getZ() + " " + text);
        }

        return saved;
    }

    // The sounds, level events and block events sent to the acting player, in order, as "kind detail".
    private static JsonArray userEffects(List<Packet<?>> packets) throws Exception {
        var effects = new JsonArray();
        for (var packet : packets) {
            if (packet instanceof ClientboundSoundPacket sound) {
                effects.add("sound " + sound.getSound().value().location());
            } else if (packet instanceof ClientboundLevelEventPacket event) {
                var at = (String) call("offset", event.getPos());
                effects.add("level_event " + event.getType() + " " + at + " " + event.getData());
            } else if (packet instanceof ClientboundBlockEventPacket event) {
                var block = BuiltInRegistries.BLOCK.getKey(event.getBlock());
                var at = (String) call("offset", event.getPos());
                effects.add("block_event " + block + " " + event.getB0() + " " + event.getB1() + " " + at);
            }
        }

        return effects;
    }

    // A probe player of BlockUseFixtures, whose statistics are recorded and whose packets go to the list.
    private static ServerPlayer probePlayer(MinecraftServer server, ServerLevel level, String name,
        List<Packet<?>> packets) throws Exception {
        var profile = new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
        var player = (ServerPlayer) construct("ProbePlayer", server, level, profile);
        player.connection = (ServerGamePacketListenerImpl) call("recorder", packets);
        return player;
    }

    @SuppressWarnings("unchecked")
    private static List<String> awarded(ServerPlayer player) throws Exception {
        var field = player.getClass().getDeclaredField("awarded");
        field.setAccessible(true);
        return (List<String>) field.get(player);
    }

    // Calls BlockUseFixtures' static method of that name taking as many arguments.
    private static Object call(String name, Object... args) throws Exception {
        for (Method method : BlockUseFixtures.class.getDeclaredMethods()) {
            if (method.getName().equals(name) && method.getParameterCount() == args.length) {
                method.setAccessible(true);
                return method.invoke(null, args);
            }
        }

        throw new NoSuchMethodException("BlockUseFixtures." + name + " with " + args.length + " arguments");
    }

    private static Object staticField(String name) throws Exception {
        var field = BlockUseFixtures.class.getDeclaredField(name);
        field.setAccessible(true);
        return field.get(null);
    }

    // Constructs BlockUseFixtures' nested class of that name with its only constructor.
    private static Object construct(String name, Object... args) throws Exception {
        var type = Class.forName(BlockUseFixtures.class.getName() + "$" + name);
        var constructor = type.getDeclaredConstructors()[0];
        constructor.setAccessible(true);
        return constructor.newInstance(args);
    }
}
