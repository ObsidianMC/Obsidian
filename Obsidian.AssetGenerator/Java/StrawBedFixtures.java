import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonNull;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import java.lang.reflect.Method;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.chat.contents.TranslatableContents;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.network.protocol.game.ClientboundSystemChatPacket;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.portal.TeleportTransition;
import net.minecraft.world.level.storage.LevelData.RespawnData;

/**
 * Records what beds do over time: a player using a straw bed or a bed, sleeping, waking (by leaving it, when the
 * night is skipped, or when its rule stops allowing sleep) and respawning, on {@code BlockUseFixtures}' dedicated
 * server, players, packet recorders, game event listener and write observer, which it calls by reflection.
 *
 * <p>Each case sets the time of day, its blocks (without updates) and where the user's respawn point is, then runs its
 * steps: {@code use} (a use-item-on of the clicked face with an empty hand), {@code tick} (that many server ticks:
 * the level's tick, then the user's, as the connection runs it), {@code leave} (the leave-bed command), {@code time}
 * (the time of day set) and {@code respawn} (where a respawn would put the user). Each step records the blocks that
 * changed, the ordered writes and destroyed blocks, what a bystander above the target heard, the game events, the
 * statistics (but those every tick awards), the user's own sounds and messages, whether the user sleeps, where it
 * stands and its respawn point.
 */
public final class StrawBedFixtures {
    // Set without neighbor or shape updates and without drops, like BlockUseFixtures' setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // Statistics every player tick awards, which say nothing about beds.
    private static final Set<String> TickStatistics = Set.of(
        "minecraft:custom minecraft:play_time",
        "minecraft:custom minecraft:total_world_time",
        "minecraft:custom minecraft:time_since_death",
        "minecraft:custom minecraft:time_since_rest");

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
                    capture(running, root, selected, Level.OVERWORLD);
                    capture(running, root, selected, Level.NETHER);
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

    private static void capture(MinecraftServer server, JsonObject root, Set<String> selected,
        ResourceKey<Level> dimension) throws Exception {
        var level = server.getLevel(dimension);
        var name = dimension.identifier().toString();
        var target = (BlockPos) call("position", root.getAsJsonArray("target"));
        var standing = root.getAsJsonArray("player");

        var userPackets = new ArrayList<Packet<?>>();
        var user = probePlayer(server, level, "User", userPackets);

        // A second player above the target hears what is broadcast around it. It spectates, so it doesn't count
        // towards the players who must sleep for the night to pass.
        var heard = new ArrayList<Packet<?>>();
        var bystander = probePlayer(server, level, "Bystander", heard);
        bystander.setGameMode(GameType.SPECTATOR);
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
            var caseDimension = input.has("dimension") ? input.get("dimension").getAsString() : "minecraft:overworld";
            if (!caseDimension.equals(name))
                continue;

            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            bystander.setGameMode(GameType.SPECTATOR);
            setUp(server, level, user, target, standing, input);

            var results = new JsonArray();
            for (var stepElement : input.getAsJsonArray("steps")) {
                var step = stepElement.getAsJsonObject();
                var before = call("snapshot", level, target);
                userPackets.clear();
                heard.clear();
                gameEvents.clear();
                awarded(user).clear();

                var observed = new JsonObject();
                observed.addProperty("checkWrites", true);
                call("observeWrites", level, observed);

                var recorded = new JsonObject();
                run(server, level, user, bystander, target, step, recorded);

                System.getProperties().remove("obsidian.blockUse.writes");
                System.getProperties().remove("obsidian.blockUse.destroyed");

                recorded.addProperty("changes", (String) call("changes", level, target, before));
                recorded.add("writes", observed.get("writes"));
                recorded.add("destroyed", observed.get("destroyed"));
                recorded.add("entities", (JsonArray) call("detailedEntities", level, target));
                call("record", recorded, heard, gameEvents);
                recorded.add("stats", statistics(user));
                recorded.add("userEffects", userEffects(userPackets));
                recorded.add("messages", messages(userPackets));
                recorded.addProperty("sleeping", user.isSleeping());
                recorded.addProperty("sleepTimer", user.getSleepTimer());
                recorded.addProperty("position", position(user, target));
                recorded.add("respawnPoint", respawnPoint(user, target));
                results.add(recorded);
            }

            input.add("results", results);
            if (user.isSleeping())
                user.stopSleepInBed(true, true);
        }

        call("clear", level, target);
        for (var player : List.of(user, bystander)) {
            players.remove(player);
            level.players().remove(player);
        }
        System.out.println("Recorded the " + name + " bed cases");
    }

    // The case's time of day, blocks and respawn point, and the user standing where the case says.
    private static void setUp(MinecraftServer server, ServerLevel level, ServerPlayer user, BlockPos target,
        JsonArray standing, JsonObject input) throws Exception {
        call("clear", level, target);
        call("discardEntities", level, target);
        setTime(server, input.has("time") ? input.get("time").getAsLong() : 18000);

        // The floor the beds stand on and the user stands beside them on.
        for (int x = -2; x <= 2; x++) {
            for (int z = -2; z <= 2; z++) {
                var floor = (BlockState) call("state", "minecraft:stone");
                level.setBlock(target.offset(x, -1, z), floor, QuietFlags);
            }
        }

        for (var setup : input.getAsJsonArray("setup")) {
            var placement = setup.getAsJsonObject();
            var pos = target.offset((BlockPos) call("position", placement.getAsJsonArray("offset")));
            var state = (BlockState) call("state", placement.get("state").getAsString());
            level.setBlock(pos, state, QuietFlags);
        }

        user.setGameMode(GameType.byName(input.has("gameMode") ? input.get("gameMode").getAsString() : "survival"));
        user.getInventory().clearContent();
        user.setShiftKeyDown(false);
        var at = input.has("player") ? input.getAsJsonArray("player") : standing;
        user.snapTo(at.get(0).getAsDouble(), at.get(1).getAsDouble(), at.get(2).getAsDouble(),
            at.get(3).getAsFloat(), at.get(4).getAsFloat());
        user.setYHeadRot(user.getYRot());

        ServerPlayer.RespawnConfig respawn = null;
        if (input.has("respawnPoint")) {
            var point = input.getAsJsonObject("respawnPoint");
            var pos = target.offset((BlockPos) call("position", point.getAsJsonArray("offset")));
            var data = RespawnData.of(level.dimension(), pos, 0, 0);
            respawn = new ServerPlayer.RespawnConfig(data, point.has("forced") && point.get("forced").getAsBoolean());
        }
        user.setRespawnPosition(respawn, false);

        // Each case starts rested, whatever the last one left.
        var sleepCounter = Player.class.getDeclaredField("sleepCounter");
        sleepCounter.setAccessible(true);
        sleepCounter.setInt(user, 0);

        // A tick lets the sky darken (or lighten) to the time set, as it does before anyone could use a bed.
        level.tick(() -> true);
    }

    private static void run(MinecraftServer server, ServerLevel level, ServerPlayer user, ServerPlayer bystander,
        BlockPos target, JsonObject step, JsonObject recorded) throws Exception {
        if (step.has("use")) {
            var use = step.getAsJsonObject("use").deepCopy();
            var result = (InteractionResult) call("use", level, user, target, use);
            call("runBlockEvents", level);
            recorded.addProperty("result", (String) call("result", result));
        } else if (step.has("tick")) {
            for (int tick = 0; tick < step.get("tick").getAsInt(); tick++) {
                level.tick(() -> true);
                user.doTick();
            }
        } else if (step.has("bystanderMode")) {
            bystander.setGameMode(GameType.byName(step.get("bystanderMode").getAsString()));
        } else if (step.has("leave")) {
            // What the leave-bed command does (ServerGamePacketListenerImpl.handlePlayerCommand).
            if (user.isSleeping())
                user.stopSleepInBed(false, true);
        } else if (step.has("time")) {
            setTime(server, step.get("time").getAsLong());
        } else if (step.has("respawn")) {
            // Where PlayerList.respawn would put the user after a death, using up what a respawn uses.
            var transition = user.findRespawnPositionAndUseSpawnBlock(true, TeleportTransition.DO_NOTHING);
            // Without its block, a respawn goes to a random spot around the world spawn, recorded as such.
            recorded.addProperty("missingRespawnBlock", transition.missingRespawnBlock());
            var pos = transition.position();
            var at = String.format(Locale.ROOT, "%.4f,%.4f,%.4f",
                pos.x - target.getX(), pos.y - target.getY(), pos.z - target.getZ());
            recorded.addProperty("respawnAt", transition.missingRespawnBlock() ? "world spawn" : at);
            recorded.addProperty("respawnLevel", transition.newLevel().dimension().identifier().toString());
        } else {
            throw new IllegalArgumentException("Unknown step " + step);
        }
    }

    private static void setTime(MinecraftServer server, long time) {
        var source = server.createCommandSourceStack().withSuppressedOutput();
        server.getCommands().performPrefixedCommand(source, "time set " + time);
    }

    // The statistics the step awarded, but the ones every tick awards.
    private static JsonArray statistics(ServerPlayer user) throws Exception {
        var kept = new ArrayList<String>();
        for (var statistic : awarded(user)) {
            var name = statistic.substring(0, statistic.lastIndexOf(' '));
            if (!TickStatistics.contains(name))
                kept.add(statistic);
        }

        return (JsonArray) call("strings", kept);
    }

    // The user's position relative to the target, to four places.
    private static String position(Player user, BlockPos target) {
        return String.format(Locale.ROOT, "%.4f,%.4f,%.4f",
            user.getX() - target.getX(), user.getY() - target.getY(), user.getZ() - target.getZ());
    }

    // The user's respawn point as "x,y,z dimension forced", relative to the target, or null.
    private static com.google.gson.JsonElement respawnPoint(ServerPlayer user, BlockPos target) {
        var config = user.getRespawnConfig();
        if (config == null)
            return JsonNull.INSTANCE;

        var data = config.respawnData();
        var pos = data.pos().subtract(target);
        var text = pos.getX() + "," + pos.getY() + "," + pos.getZ() + " " + data.dimension().identifier()
            + (config.forced() ? " forced" : "");
        return new com.google.gson.JsonPrimitive(text);
    }

    // The system and overlay messages sent to the user, as "overlay key" or "chat key", but advancement announcements,
    // which only the first case to earn one gets.
    private static JsonArray messages(List<Packet<?>> packets) {
        var messages = new JsonArray();
        for (var packet : packets) {
            if (!(packet instanceof ClientboundSystemChatPacket chat))
                continue;

            var contents = chat.content().getContents();
            var key = contents instanceof TranslatableContents translatable
                ? translatable.getKey()
                : chat.content().getString();
            if (!key.startsWith("chat.type.advancement."))
                messages.add((chat.overlay() ? "overlay " : "chat ") + key);
        }

        return messages;
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
        player.connection.player = player;
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
