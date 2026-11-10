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
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.gameevent.GameEventListener;

/** Food and harvest captures using block_use's real server, write observer and packet recorders. */
public final class FoodHarvestFixtures {
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
        bystander.setGameMode(net.minecraft.world.level.GameType.SPECTATOR);
        bystander.snapTo(target.getX() + 0.5, target.getY() + 3, target.getZ() + 0.5, 0, 0);
        // Both are in the level's players, so broadcasts reach the user too unless they leave it out.
        @SuppressWarnings("unchecked")
        var players = (List<ServerPlayer>) call("players", server.getPlayerList());
        for (var player : List.of(user, bystander)) {
            players.add(player);
            level.addNewPlayer(player);
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
            user.getFoodData().setFoodLevel(input.has("food") ? input.get("food").getAsInt() : 20);
            user.getFoodData().setSaturation(input.has("saturation") ? input.get("saturation").getAsFloat() : 5);
            var seed = input.has("randomSeed") ? input.get("randomSeed").getAsLong() : 1234;
            level.getRandom().setSeed(seed);
            for (var table : List.of("harvest/sweet_berry_bush", "harvest/cave_vine", "harvest/beehive", "carve/pumpkin"))
                server.getRandomSequence(net.minecraft.resources.Identifier.parse("minecraft:" + table)).setSeed(seed);
            if (input.has("nearbyBee")) {
                var bee = new net.minecraft.world.entity.animal.bee.Bee(
                    (net.minecraft.world.entity.EntityType) BuiltInRegistries.ENTITY_TYPE.getValue(
                        net.minecraft.resources.Identifier.parse("minecraft:bee")), level);
                var offset = input.has("nearbyBeeOffset") ? input.getAsJsonArray("nearbyBeeOffset") : null;
                var x = offset == null ? 1.5 : offset.get(0).getAsDouble();
                var y = offset == null ? 0 : offset.get(1).getAsDouble();
                var z = offset == null ? 0.5 : offset.get(2).getAsDouble();
                bee.snapTo(target.getX() + x, target.getY() + y, target.getZ() + z, 0, 0);
                level.addFreshEntity(bee);
            }

            var before = call("snapshot", level, target);
            userPackets.clear();
            heard.clear();
            gameEvents.clear();
            awarded(user).clear();

            if (input.has("shippedAdvancement")) {
                var name = input.get("shippedAdvancement").getAsString();
                var path = net.minecraft.resources.Identifier.parse("minecraft:advancement/" + name + ".json");
                var resource = server.getResourceManager().getResource(path).orElseThrow();
                try (var reader = resource.openAsReader()) {
                    var shipped = JsonParser.parseReader(reader).getAsJsonObject();
                    input.add("criteria", shipped.getAsJsonObject("criteria").deepCopy());
                }
            }
            var advancement = (AdvancementHolder) call("prepareCriteria", level, user, input);
            var hand = (InteractionHand) call("hand", input);
            var usedStack = user.getItemInHand(hand);
            if (input.has("damage"))
                usedStack.setDamageValue(input.get("damage").getAsInt());

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
            input.addProperty("hand", (String) call("stack", user.getItemInHand(hand)));
            input.addProperty("foodAfter", user.getFoodData().getFoodLevel());
            input.addProperty("saturationAfter", user.getFoodData().getSaturationLevel());
            input.add("inventory", inventory(user));
            input.addProperty("smokey", net.minecraft.world.level.block.CampfireBlock.isSmokeyPos(level, target));
            input.add("bees", bees(level, target, user));
            if (input.has("checkDropPositions"))
                input.add("dropPositions", dropPositions(level, target));
            if (input.has("checkDropMotion"))
                input.add("dropMotion", dropMotion(level, target));
            call("recordExtra", input, heard);
            input.add("entities", entities(level, target));
            call("record", input, heard, gameEvents);
            input.add("stats", (JsonArray) call("strings", awarded(user)));
            input.add("userEffects", userEffects(userPackets));
            input.add("soundPackets", soundPackets(heard));
            input.add("writes", observed.get("writes"));
            input.add("destroyed", observed.get("destroyed"));
        }

        call("clear", level, target);
        for (var player : List.of(user, bystander)) {
            players.remove(player);
            level.players().remove(player);
        }
        System.out.println("Used " + root.getAsJsonArray("cases").size() + " blocks");
    }

    // The case's blocks in a cleared area, set without updates.
    private static void setUp(ServerLevel level, BlockPos target, JsonObject input) throws Exception {
        call("clear", level, target);
        for (int depth = 4; depth <= 7; depth++)
            level.setBlock(target.below(depth), net.minecraft.world.level.block.Blocks.AIR.defaultBlockState(), QuietFlags);
        for (var entity : level.getEntities((net.minecraft.world.entity.Entity)null,
            new net.minecraft.world.phys.AABB(target).inflate(10), entity -> !(entity instanceof ServerPlayer)))
            entity.discard();
        for (var setup : input.getAsJsonArray("setup")) {
            var placement = setup.getAsJsonObject();
            var offset = (BlockPos) call("position", placement.getAsJsonArray("offset"));
            var state = (BlockState) call("state", placement.get("state").getAsString());
            level.setBlock(target.offset(offset), state, QuietFlags);
            if (placement.has("nbt"))
                call("loadData", level, target.offset(offset), placement.get("nbt").getAsString());
        }
    }

    private static JsonArray entities(ServerLevel level, BlockPos target) throws Exception {
        var result = new JsonArray();
        for (var entity : level.getEntities((net.minecraft.world.entity.Entity)null,
            new net.minecraft.world.phys.AABB(target).inflate(3), entity -> !(entity instanceof ServerPlayer))) {
            if (entity instanceof net.minecraft.world.entity.item.ItemEntity item)
                result.add("item " + call("stack", item.getItem()));
            else
                result.add(BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
        }
        return result;
    }

    private static JsonArray inventory(ServerPlayer user) throws Exception {
        var result = new JsonArray();
        for (int slot = 0; slot < 41; slot++) {
            var item = user.getInventory().getItem(slot);
            if (!item.isEmpty())
                result.add(slot + "=" + call("stack", item));
        }
        return result;
    }

    private static JsonArray dropPositions(ServerLevel level, BlockPos target) throws Exception {
        var result = new JsonArray();
        for (var entity : level.getEntities((net.minecraft.world.entity.Entity)null,
            new net.minecraft.world.phys.AABB(target).inflate(4), entity -> true)) {
            if (entity instanceof net.minecraft.world.entity.item.ItemEntity item) {
                result.add(call("stack", item.getItem()) + " at "
                    + (item.getX() - target.getX()) + "," + (item.getY() - target.getY()) + ","
                    + (item.getZ() - target.getZ()));
            }
        }
        return result;
    }

    private static JsonArray dropMotion(ServerLevel level, BlockPos target) throws Exception {
        var result = new JsonArray();
        for (var entity : level.getEntities((net.minecraft.world.entity.Entity)null,
            new net.minecraft.world.phys.AABB(target).inflate(4), entity -> true)) {
            if (entity instanceof net.minecraft.world.entity.item.ItemEntity item) {
                var motion = item.getDeltaMovement();
                var value = new JsonObject();
                value.addProperty("item", (String) call("stack", item.getItem()));
                value.addProperty("x", motion.x);
                value.addProperty("y", motion.y);
                value.addProperty("z", motion.z);
                result.add(value);
            }
        }
        return result;
    }

    private static JsonArray bees(ServerLevel level, BlockPos target, ServerPlayer user) {
        var result = new JsonArray();
        if (level.getBlockEntity(target) instanceof net.minecraft.world.level.block.entity.BeehiveBlockEntity hive)
            result.add("occupants=" + hive.getOccupantCount());

        for (var entity : level.getEntities((net.minecraft.world.entity.Entity)null,
            new net.minecraft.world.phys.AABB(target).inflate(20), entity -> true)) {
            if (entity instanceof net.minecraft.world.entity.animal.bee.Bee bee)
                result.add("bee target=" + (bee.getTarget() == user) + " angry=" + bee.isAngry());
        }
        return result;
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
    private static JsonArray soundPackets(List<Packet<?>> packets) {
        var result = new JsonArray();
        for (var packet : packets) {
            if (packet instanceof ClientboundSoundPacket sound) {
                var value = new JsonObject();
                value.addProperty("id", sound.getSound().value().location().toString());
                value.addProperty("x", (int)(sound.getX() * 8));
                value.addProperty("y", (int)(sound.getY() * 8));
                value.addProperty("z", (int)(sound.getZ() * 8));
                value.addProperty("volume", sound.getVolume());
                value.addProperty("pitch", sound.getPitch());
                value.addProperty("category", sound.getSource().name().toLowerCase(java.util.Locale.ROOT));
                result.add(value);
            }
        }
        return result;
    }

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
        var sender = ServerGamePacketListenerImpl.class.getDeclaredField("chunkSender");
        sender.setAccessible(true);
        sender.set(player.connection, new net.minecraft.server.network.PlayerChunkSender(true));
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
