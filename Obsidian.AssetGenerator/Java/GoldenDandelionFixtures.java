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
import java.util.Locale;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundLevelParticlesPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.phys.Vec3;

/**
 * Records golden dandelions locking and unlocking a young mob's age, and what a locked age stops, on
 * {@code BlockUseFixtures}' dedicated server, players, packet recorders and game event listener, which it calls by
 * reflection.
 *
 * <p>Each case spawns its {@code entity} on a stone floor at the target, a baby unless {@code baby} is false, with
 * the given {@code age} and {@code ageLocked}, and gives the user its hands. Its steps are {@code interact} (what the
 * server does with an interaction packet once the mob is in reach: {@code Player.interactOn}) and {@code tick} (that
 * many of the mob's ticks). Each step records the result of an interaction, the hand after it, the sounds a bystander
 * heard, the age-related particles it was sent (pausing and resetting growth, and the growth a feed gives), the game
 * events, the statistics and the mob's type and saved age fields.
 */
public final class GoldenDandelionFixtures {
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // The saved fields about a mob's age.
    private static final String[] AgeFields = { "Age", "ForcedAge", "AgeLocked", "PersistenceRequired" };

    // The particles about a mob's age and growth; mobs send others (hearts, smoke) that say nothing about it.
    private static final Set<String> AgeParticles = Set.of(
        "minecraft:pause_mob_growth", "minecraft:reset_mob_growth", "minecraft:happy_villager");

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

            call("clear", level, target);
            call("discardEntities", level, target);
            var floor = (BlockState) call("state", "minecraft:stone");
            for (int x = -2; x <= 2; x++) {
                for (int z = -2; z <= 2; z++)
                    level.setBlock(target.offset(x, -1, z), floor, QuietFlags);
            }

            call("prepare", user, standing, input);
            var mob = spawn(level, target, input);

            var results = new JsonArray();
            for (var stepElement : input.getAsJsonArray("steps")) {
                var step = stepElement.getAsJsonObject();
                userPackets.clear();
                heard.clear();
                gameEvents.clear();
                awarded(user).clear();

                var recorded = new JsonObject();
                if (step.has("interact")) {
                    var hand = (InteractionHand) call("hand", input);
                    var result = user.interactOn(mob, hand, new Vec3(0, 0.3, 0));
                    recorded.addProperty("result", (String) call("result", result));
                    recorded.addProperty("hand", (String) call("stack", user.getItemInHand(hand)));
                } else {
                    for (int tick = 0; tick < step.get("tick").getAsInt(); tick++) {
                        if (!mob.isRemoved())
                            level.tickNonPassenger(mob);
                    }
                }

                recorded.add("sounds", sounds(heard));
                recorded.add("particles", particles(heard));
                recorded.add("gameEvents", (JsonArray) call("strings", new ArrayList<>(gameEvents)));
                recorded.add("stats", (JsonArray) call("strings", awarded(user)));
                recorded.addProperty("mob", describe(level, mob));
                results.add(recorded);
            }

            input.add("results", results);
        }

        call("clear", level, target);
        call("discardEntities", level, target);
        for (var player : List.of(user, bystander)) {
            players.remove(player);
            level.players().remove(player);
        }
        System.out.println("Recorded " + root.getAsJsonArray("cases").size() + " golden dandelion cases");
    }

    // The case's mob at the centre of the target, young unless the case says otherwise, with its age and lock set as
    // a save would load them.
    private static Mob spawn(ServerLevel level, BlockPos target, JsonObject input) throws Exception {
        var type = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse(input.get("entity").getAsString()));
        var mob = (Mob) type.create(level, EntitySpawnReason.COMMAND);
        mob.snapTo(target.getX() + 0.5, target.getY(), target.getZ() + 0.5, 0, 0);

        var baby = !input.has("baby") || input.get("baby").getAsBoolean();
        if (input.has("age")) {
            invoke(mob, "setAge", int.class, input.get("age").getAsInt());
        } else if (baby && mob instanceof net.minecraft.world.entity.AgeableMob) {
            mob.setBaby(true);
        }

        if (input.has("ageLocked"))
            invoke(mob, "setAgeLocked", boolean.class, input.get("ageLocked").getAsBoolean());

        level.addFreshEntity(mob);
        return mob;
    }

    // Calls the mob's own or inherited method of that name and parameter type.
    private static void invoke(Entity mob, String name, Class<?> parameter, Object value) throws Exception {
        for (Class<?> type = mob.getClass(); type != null; type = type.getSuperclass()) {
            try {
                var method = type.getDeclaredMethod(name, parameter);
                method.setAccessible(true);
                method.invoke(mob, value);
                return;
            } catch (NoSuchMethodException e) {
                // Declared further up.
            }
        }

        throw new NoSuchMethodException(mob.getClass().getName() + "." + name);
    }

    // The mob's type and saved age fields, or what replaced it when it grew up into another type.
    private static String describe(ServerLevel level, Mob mob) {
        Entity current = mob;
        if (mob.isRemoved()) {
            var replaced = level.getEntities((Entity) null, mob.getBoundingBox().inflate(2), entity ->
                !(entity instanceof ServerPlayer) && entity != mob);
            if (replaced.isEmpty())
                return "removed";

            current = replaced.get(0);
        }

        var saved = newOutput(level);
        current.saveWithoutId(saved);
        var text = new StringBuilder(BuiltInRegistries.ENTITY_TYPE.getKey(current.getType()).toString());
        var tag = saved.buildResult();
        for (var field : AgeFields) {
            if (tag.contains(field))
                text.append(' ').append(field).append('=').append(tag.get(field));
        }

        return text.toString();
    }

    private static net.minecraft.world.level.storage.TagValueOutput newOutput(ServerLevel level) {
        return net.minecraft.world.level.storage.TagValueOutput.createWithContext(
            net.minecraft.util.ProblemReporter.DISCARDING, level.registryAccess());
    }

    // The sounds the bystander heard, as "id source volume pitch".
    private static JsonArray sounds(List<Packet<?>> packets) {
        var sounds = new JsonArray();
        for (var packet : packets) {
            if (packet instanceof ClientboundSoundPacket sound) {
                sounds.add(String.format(Locale.ROOT, "%s %s %.1f %.1f",
                    sound.getSound().value().location(),
                    sound.getSource().getName(),
                    sound.getVolume(),
                    sound.getPitch()));
            }
        }

        return sounds;
    }

    // The age-related particle packets the bystander was sent, as "type count", in order.
    private static JsonArray particles(List<Packet<?>> packets) {
        var particles = new JsonArray();
        for (var packet : packets) {
            if (!(packet instanceof ClientboundLevelParticlesPacket particle))
                continue;

            var type = BuiltInRegistries.PARTICLE_TYPE.getKey(particle.particle().getType()).toString();
            if (AgeParticles.contains(type))
                particles.add(type + " " + particle.count());
        }

        return particles;
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
