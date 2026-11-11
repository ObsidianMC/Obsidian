import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
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
import net.minecraft.advancements.AdvancementHolder;
import net.minecraft.advancements.triggers.CriteriaTriggers;
import net.minecraft.core.BlockPos;
import net.minecraft.core.SectionPos;
import net.minecraft.core.component.DataComponentPatch;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.NbtOps;
import net.minecraft.nbt.TagParser;
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
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.gameevent.GameEventListener;

/**
 * Records using cauldrons through {@code ServerPlayerGameMode.useItemOn}, the routine behind a use-item-on packet:
 * filling and emptying them with buckets and bottles, and washing dyed armor, banners and shulker boxes in them.
 *
 * <p>The cases use {@code block_use.json}'s input format, and the probe runs on {@code BlockUseFixtures}' dedicated
 * server, players, packet recorders, game event listener and write observer, which it calls by reflection so that
 * probe stays as it is. Every case records what that probe records with {@code checkWrites}, {@code checkEffects}
 * and {@code checkInventory}, and also what the acting player's own client was sent ({@code userEffects}), both hands
 * with their components, and the items that dropped with theirs.
 *
 * <p>Inputs beyond {@code block_use.json}'s: {@code components} and {@code offhandComponents} are SNBT component
 * patches for the stack in each hand, and {@code carried} puts further stacks into inventory slots (vanilla's slot
 * numbers, the hotbar being 0 to 8) before the use.
 */
public final class CauldronFixtures {
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
        // Both are in the level's players, so broadcasts reach the user too unless they leave it out.
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
            call("prepare", user, standing, input);
            applyComponents(level, user, InteractionHand.MAIN_HAND, input, "components");
            applyComponents(level, user, InteractionHand.OFF_HAND, input, "offhandComponents");
            carry(user, input);

            var before = call("snapshot", level, target);
            userPackets.clear();
            heard.clear();
            gameEvents.clear();
            awarded(user).clear();

            var advancement = (AdvancementHolder) call("prepareCriteria", level, user, input);
            var hand = (InteractionHand) call("hand", input);
            var usedStack = user.getItemInHand(hand);

            // The write observer reads its switch from the object it is given and records into it.
            var observed = new JsonObject();
            observed.addProperty("checkWrites", true);
            call("observeWrites", level, observed);

            var result = (InteractionResult) call("use", level, user, target, input);
            if (advancement != null && result.consumesAction()) {
                // The packet handler, not useItemOn, fires any_block_use, with the item in hand after the use.
                var clicked = target.offset((BlockPos) call("position", input.getAsJsonArray("clicked")));
                CriteriaTriggers.ANY_BLOCK_USE.trigger(user, clicked, usedStack);
            }

            call("runBlockEvents", level);
            System.getProperties().remove("obsidian.blockUse.writes");
            System.getProperties().remove("obsidian.blockUse.destroyed");

            if (advancement != null)
                input.add("awardedCriteria", awardedCriteria(user, advancement, input));

            input.addProperty("result", (String) call("result", result));
            input.addProperty("changes", (String) call("changes", level, target, before));
            input.addProperty("hand", (String) call("detailedStack", level, user.getItemInHand(hand)));
            var other = hand == InteractionHand.MAIN_HAND ? InteractionHand.OFF_HAND : InteractionHand.MAIN_HAND;
            input.addProperty("otherHand", (String) call("detailedStack", level, user.getItemInHand(other)));
            input.add("inventory", inventory(level, user));
            call("recordExtra", input, heard);
            input.add("entities", (JsonArray) call("detailedEntities", level, target));
            call("record", input, heard, gameEvents);
            input.add("stats", (JsonArray) call("strings", awarded(user)));
            input.add("userEffects", userEffects(userPackets));
            input.add("writes", observed.get("writes"));
            input.add("destroyed", observed.get("destroyed"));
        }

        call("clear", level, target);
        for (var player : List.of(user, bystander)) {
            players.remove(player);
            level.players().remove(player);
        }
        System.out.println("Used " + root.getAsJsonArray("cases").size() + " cauldrons");
    }

    // The case's blocks in a cleared area, set without updates.
    private static void setUp(ServerLevel level, BlockPos target, JsonObject input) throws Exception {
        call("clear", level, target);
        call("discardEntities", level, target);
        for (var setup : input.getAsJsonArray("setup")) {
            var placement = setup.getAsJsonObject();
            var offset = (BlockPos) call("position", placement.getAsJsonArray("offset"));
            var state = (BlockState) call("state", placement.get("state").getAsString());
            level.setBlock(target.offset(offset), state, QuietFlags);
        }
    }

    // Applies an SNBT component patch to the stack in the hand, when the case gives one.
    private static void applyComponents(ServerLevel level, ServerPlayer user, InteractionHand hand, JsonObject input,
        String name) throws Exception {
        if (!input.has(name))
            return;

        var ops = level.registryAccess().createSerializationContext(NbtOps.INSTANCE);
        var tag = TagParser.parseCompoundFully(input.get(name).getAsString());
        var patch = DataComponentPatch.CODEC.parse(ops, tag).getOrThrow();
        user.getItemInHand(hand).applyComponents(patch);
    }

    // The case's further stacks, each put into its inventory slot.
    private static void carry(ServerPlayer user, JsonObject input) {
        if (!input.has("carried"))
            return;

        for (var element : input.getAsJsonArray("carried")) {
            var carried = element.getAsJsonObject();
            var item = BuiltInRegistries.ITEM.getValue(Identifier.parse(carried.get("item").getAsString()));
            var stack = new ItemStack(item, carried.get("count").getAsInt());
            user.getInventory().setItem(carried.get("slot").getAsInt(), stack);
        }
    }

    // All 36 storage slots in vanilla's order, each stack with the components it changes from its item's.
    private static JsonArray inventory(ServerLevel level, ServerPlayer user) throws Exception {
        var slots = new JsonArray();
        for (int slot = 0; slot < 36; slot++)
            slots.add((String) call("detailedStack", level, user.getInventory().getItem(slot)));

        return slots;
    }

    private static JsonArray awardedCriteria(ServerPlayer user, AdvancementHolder advancement, JsonObject input) {
        var progress = user.getAdvancements().getOrStartProgress(advancement);
        var awarded = new JsonArray();
        for (var name : input.getAsJsonObject("criteria").keySet()) {
            if (progress.getCriterion(name).isDone())
                awarded.add(name);
        }

        return awarded;
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
