import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
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
import net.minecraft.core.Direction;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.IntArrayTag;
import net.minecraft.nbt.Tag;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelParticlesPacket;
import net.minecraft.network.protocol.game.ClientboundOpenScreenPacket;
import net.minecraft.network.protocol.game.ClientboundSoundEntityPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.network.protocol.game.ClientboundUpdateMobEffectPacket;
import net.minecraft.network.protocol.game.ServerboundPlayerActionPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.storage.ServerLevelData;
import net.minecraft.world.phys.AABB;

/**
 * Records the special blocks of the block interactions plan's phase 6: ender chests and shulker boxes opening and
 * refusing to, the dragon egg teleporting when used and attacked, conduits, End gateways and opening shulker boxes
 * pushing what stands on their lids.
 *
 * <p>The cases use {@code block_use.json}'s input format, and the probe runs on {@code BlockUseFixtures}' dedicated
 * server, players, packet recorders, game event listener and write observer, which it calls by reflection so that
 * probe stays as it is. A use case records what that probe records with {@code checkWrites} and {@code checkEffects},
 * plus the effects sent to the acting player's own client; {@code attack} starts hitting the clicked block instead,
 * as {@code ServerPlayerGameMode.handleBlockBreakAction} does for a survival player. {@code randomSeed} seeds the
 * level's random first, which the dragon egg draws its destination from.
 *
 * <p>A case with {@code ticks} runs that many whole level ticks instead: the game is frozen and stepped one tick at a
 * time, the user is a player of the level (so blocks and entities find it), standing at the case's {@code player},
 * whose own tick runs after the level's as a connection's does. An optional {@code use} uses the case's item first,
 * and {@code close} closes the menu it opened after that many ticks. Each tick that did something records the writes
 * with their flags, the sounds, level events, block events, particles and game events, the entities around the
 * target with their positions and health, the effects the user was given, the user's position when it moved, and the
 * client data of the target's block entity when it changed.
 */
public final class SpecialBlockFixtures {
    // Set without neighbor or shape updates and without drops, like BlockUseFixtures' setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // How far around the target a tick case's entities are recorded and discarded.
    private static final int EntityReach = 16;

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

        // Nothing moves unless stepped, and nothing else draws on the level's random or spawns.
        server.tickRateManager().setFrozen(true);
        var rules = server.getGameRules();
        rules.set(GameRules.ADVANCE_WEATHER, false, server);
        rules.set(GameRules.SPAWN_MOBS, false, server);
        rules.set(GameRules.RANDOM_TICK_SPEED, 0, server);

        var probe = new Probe(server, level, target, root.getAsJsonArray("player"));
        var count = 0;
        for (var element : root.getAsJsonArray("cases")) {
            var input = element.getAsJsonObject();
            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            if (input.has("ticks"))
                probe.tickCase(input);
            else
                probe.useCase(input);

            count++;
        }

        call("clear", level, target);
        probe.leave();
        System.out.println("Recorded " + count + " special block cases");
    }

    /** The server's level, the user and the bystander, and what they hear. */
    private static final class Probe {
        final MinecraftServer server;
        final ServerLevel level;
        final BlockPos target;
        final JsonArray standing;
        final ServerPlayer user;
        final ServerPlayer bystander;
        final List<Packet<?>> userPackets = new ArrayList<>();
        final List<Packet<?>> heard = new ArrayList<>();
        final List<String> gameEvents = new ArrayList<>();
        final List<ServerPlayer> playerList;

        Probe(MinecraftServer server, ServerLevel level, BlockPos target, JsonArray standing) throws Exception {
            this.server = server;
            this.level = level;
            this.target = target;
            this.standing = standing;
            user = probePlayer("User", userPackets);

            // A second player above the target hears what is broadcast around it.
            bystander = probePlayer("Bystander", heard);
            bystander.snapTo(target.getX() + 0.5, target.getY() + 3, target.getZ() + 0.5, 0, 0);
            @SuppressWarnings("unchecked")
            var players = (List<ServerPlayer>) call("players", server.getPlayerList());
            playerList = players;
            for (var player : List.of(user, bystander)) {
                players.add(player);
                level.players().add(player);
            }

            var listener = (GameEventListener) construct("Listener", target, user, gameEvents);
            var section = SectionPos.blockToSectionCoord(target.getY());
            level.getChunkAt(target).getListenerRegistry(section).register(listener);
        }

        void leave() {
            for (var player : List.of(user, bystander)) {
                playerList.remove(player);
                level.players().remove(player);
            }
        }

        // A use, or an attack, recorded as BlockUseFixtures records a use with checkWrites and checkEffects.
        void useCase(JsonObject input) throws Exception {
            setUp(input);
            call("prepare", user, standing, input);

            var before = call("snapshot", level, target);
            forget();
            ((List<?>) awarded(user)).clear();

            var hand = (InteractionHand) call("hand", input);
            if (input.has("randomSeed"))
                level.getRandom().setSeed(input.get("randomSeed").getAsLong());

            // The write observer reads its switch from the object it is given and records into it.
            var observed = new JsonObject();
            observed.addProperty("checkWrites", true);
            call("observeWrites", level, observed);

            InteractionResult result = null;
            if (input.has("attack") && input.get("attack").getAsBoolean())
                attack(input);
            else
                result = (InteractionResult) call("use", level, user, target, input);

            call("runBlockEvents", level);
            stopObserving();

            if (result != null)
                input.addProperty("result", (String) call("result", result));

            input.addProperty("changes", (String) call("changes", level, target, before));
            input.addProperty("hand", (String) call("stack", user.getItemInHand(hand)));
            call("recordExtra", input, heard);
            input.add("entities", (JsonArray) call("entities", level, target));
            call("record", input, heard, gameEvents);
            input.add("stats", (JsonArray) call("strings", awarded(user)));
            input.add("userEffects", userEffects());
            input.add("writes", observed.get("writes"));
            input.add("destroyed", observed.get("destroyed"));

            input.add("menu", null);
            for (var packet : userPackets) {
                if (packet instanceof ClientboundOpenScreenPacket screen)
                    input.addProperty("menu", BuiltInRegistries.MENU.getKey(screen.getType()).toString());
            }

            if (user.containerMenu != user.inventoryMenu) {
                var afterUse = call("snapshot", level, target);
                forget();
                user.closeContainer();
                call("runBlockEvents", level);

                var closed = new JsonObject();
                closed.addProperty("changes", (String) call("changes", level, target, afterUse));
                call("record", closed, heard, gameEvents);
                input.add("closed", closed);
            }
        }

        // The start of hitting the clicked block, as a survival player's start-destroy action runs it.
        private void attack(JsonObject input) throws Exception {
            var clicked = target.offset((BlockPos) call("position", input.getAsJsonArray("clicked")));
            var face = Direction.valueOf(input.get("face").getAsString().toUpperCase(Locale.ROOT));
            user.gameMode.handleBlockBreakAction(clicked, ServerboundPlayerActionPacket.Action.START_DESTROY_BLOCK,
                face, level.getMaxY(), 0);

            // Stops hitting it again, so the user's later ticks show no progress.
            user.gameMode.handleBlockBreakAction(clicked, ServerboundPlayerActionPacket.Action.ABORT_DESTROY_BLOCK,
                face, level.getMaxY(), 0);
        }

        // Whole level ticks, each recorded when it did something.
        void tickCase(JsonObject input) throws Exception {
            setUp(input);
            call("prepare", user, standing, input);
            if (input.has("gameTime"))
                setGameTime(input.get("gameTime").getAsLong());

            // The user is one of the level's entities for the case, so conduits and gateways find it.
            var unsetRemoved = Entity.class.getDeclaredMethod("unsetRemoved");
            unsetRemoved.setAccessible(true);
            unsetRemoved.invoke(user);
            level.addNewPlayer(user);
            user.setYHeadRot(user.getYRot());

            // It flies, so its own tick doesn't move it: the server simulates its players' falls.
            user.getAbilities().mayfly = true;
            user.getAbilities().flying = true;
            fresh(user);

            if (input.has("entities")) {
                for (var value : input.getAsJsonArray("entities"))
                    spawn(value.getAsJsonObject());
            }

            forget();
            if (input.has("use")) {
                var use = input.getAsJsonObject("use");
                var result = (InteractionResult) call("use", level, user, target, use);
                input.addProperty("useResult", (String) call("result", result));
            }

            var writes = new ArrayList<String>();
            observeWrites(writes);
            var checkpoints = new JsonArray();
            var previousData = blockEntityData();
            var previousPosition = userPosition();
            var close = input.has("close") ? input.get("close").getAsInt() : -1;
            try {
                for (int tick = 1; tick <= input.get("ticks").getAsInt(); tick++) {
                    if (tick == close)
                        user.closeContainer();

                    step();

                    var checkpoint = new JsonObject();
                    checkpoint.addProperty("tick", tick);
                    var changed = false;
                    changed |= add(checkpoint, "writes", writes);
                    changed |= recordEffects(checkpoint);
                    var data = blockEntityData();
                    if (!data.equals(previousData)) {
                        checkpoint.addProperty("blockEntity", data);
                        previousData = data;
                        changed = true;
                    }

                    var position = userPosition();
                    if (!position.equals(previousPosition)) {
                        checkpoint.addProperty("user", position);
                        previousPosition = position;
                        changed = true;
                    }

                    var entities = entities();
                    checkpoint.add("entities", entities);
                    if (changed || tick == input.get("ticks").getAsInt())
                        checkpoints.add(checkpoint);

                    writes.clear();
                    forget();
                }
            } finally {
                stopObserving();
                if (user.containerMenu != user.inventoryMenu)
                    user.closeContainer();

                // What closing queued runs now, not in the next case.
                call("runBlockEvents", level);

                user.getAbilities().flying = false;
                user.getAbilities().mayfly = false;
                // Out of the level's entities again, but still among its players, as the use cases have it.
                level.removePlayerImmediately(user, Entity.RemovalReason.DISCARDED);
                level.players().remove(user);
                level.players().add(user);
            }

            input.add("checkpoints", checkpoints);
        }

        // The user as a player who just joined: nothing carried over from the last case, such as a portal
        // cooldown, its effects, or having been in water.
        private void fresh(ServerPlayer player) throws Exception {
            player.setPortalCooldown(0);
            player.removeAllEffects();
            player.setDeltaMovement(net.minecraft.world.phys.Vec3.ZERO);
            player.resetFallDistance();
            var portal = Entity.class.getDeclaredField("portalProcess");
            portal.setAccessible(true);
            portal.set(player, null);
            var firstTick = Entity.class.getDeclaredField("firstTick");
            firstTick.setAccessible(true);
            firstTick.setBoolean(player, true);
        }

        // One game tick of the level, then the user's own tick, as the server runs its connections after its levels.
        private void step() {
            var manager = server.tickRateManager();
            manager.setFrozenTicksToRun(1);
            manager.tick();
            level.tick(() -> true);
            manager.tick();
            user.doTick();
        }

        private void setGameTime(long time) throws Exception {
            var field = ServerLevel.class.getDeclaredField("serverLevelData");
            field.setAccessible(true);
            ((ServerLevelData) field.get(level)).setGameTime(time);
        }

        // A mob without AI unless the case gives it one, standing where the case says, facing south; a silent one
        // makes no sounds of its own, such as the ambient sounds vanilla draws from its own random.
        private void spawn(JsonObject input) {
            var type = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse(input.get("type").getAsString()));
            var entity = type.create(level, EntitySpawnReason.COMMAND);
            var offset = input.getAsJsonArray("offset");
            entity.snapTo(
                target.getX() + offset.get(0).getAsDouble(),
                target.getY() + offset.get(1).getAsDouble(),
                target.getZ() + offset.get(2).getAsDouble(),
                0,
                0);
            entity.setNoGravity(input.has("noGravity") && input.get("noGravity").getAsBoolean());
            entity.setSilent(input.has("silent") && input.get("silent").getAsBoolean());
            if (entity instanceof Mob mob) {
                mob.setNoAi(!input.has("ai") || !input.get("ai").getAsBoolean());
                mob.setPersistenceRequired();
                mob.setYHeadRot(0);
                mob.setYBodyRot(0);
            }

            level.addFreshEntity(entity);
        }

        // The case's blocks, with their block entity data, in a cleared area without entities.
        private void setUp(JsonObject input) throws Exception {
            call("clear", level, target);
            for (var entity : level.getEntities((Entity) null, new AABB(target).inflate(EntityReach),
                entity -> !(entity instanceof Player))) {
                entity.discard();
            }

            for (var setup : input.getAsJsonArray("setup")) {
                var placement = setup.getAsJsonObject();
                var offset = (BlockPos) call("position", placement.getAsJsonArray("offset"));
                var state = (BlockState) call("state", placement.get("state").getAsString());
                var pos = target.offset(offset);
                level.setBlock(pos, state, QuietFlags);
                if (placement.has("nbt"))
                    call("loadData", level, pos, placement.get("nbt").getAsString());
            }

            if (input.has("cats")) {
                for (var value : input.getAsJsonArray("cats")) {
                    var catInput = value.getAsJsonObject();
                    var type = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse("minecraft:cat"));
                    var cat = (net.minecraft.world.entity.animal.feline.Cat) type.create(level,
                        EntitySpawnReason.COMMAND);
                    var at = target.offset((BlockPos) call("position", catInput.getAsJsonArray("offset")));
                    cat.snapTo(at.getX() + 0.5, at.getY(), at.getZ() + 0.5, 0, 0);
                    cat.setInSittingPose(catInput.get("sitting").getAsBoolean());
                    level.addFreshEntity(cat);
                }
            }
        }

        private void forget() {
            userPackets.clear();
            heard.clear();
            gameEvents.clear();
        }

        // Every write of the level on this thread, as "dx,dy,dz=state flags n".
        private void observeWrites(List<String> writes) throws Exception {
            var thread = Thread.currentThread();
            java.util.function.Consumer<Object[]> observer = values -> {
                if (values[0] != level || Thread.currentThread() != thread)
                    return;

                try {
                    var at = (String) call("offset", (BlockPos) values[1]);
                    var state = (String) call("text", (BlockState) values[2]);
                    writes.add(at + "=" + state + " flags " + values[3]);
                } catch (Exception e) {
                    throw new RuntimeException(e);
                }
            };
            System.getProperties().put("obsidian.blockUse.writes", observer);
        }

        private void stopObserving() {
            System.getProperties().remove("obsidian.blockUse.writes");
            System.getProperties().remove("obsidian.blockUse.destroyed");
        }

        // The tick's sounds (where they play), level events, block events and particles the bystander heard, its game
        // events and the effects the user was given; whether there was any.
        private boolean recordEffects(JsonObject checkpoint) throws Exception {
            var sounds = new ArrayList<String>();
            var levelEvents = new ArrayList<String>();
            var blockEvents = new ArrayList<String>();
            var particles = new ArrayList<String>();
            for (var packet : heard) {
                if (packet instanceof ClientboundSoundPacket sound) {
                    var at = BlockPos.containing(sound.getX(), sound.getY(), sound.getZ());
                    sounds.add(sound.getSound().value().location() + " " + call("offset", at));
                } else if (packet instanceof ClientboundSoundEntityPacket sound) {
                    sounds.add(sound.getSound().value().location() + " entity");
                } else if (packet instanceof ClientboundLevelEventPacket event) {
                    levelEvents.add(event.getType() + " " + call("offset", event.getPos()) + " " + event.getData());
                } else if (packet instanceof ClientboundBlockEventPacket event) {
                    var block = BuiltInRegistries.BLOCK.getKey(event.getBlock());
                    blockEvents.add(block + " " + event.getB0() + " " + event.getB1() + " "
                        + call("offset", event.getPos()));
                } else if (packet instanceof ClientboundLevelParticlesPacket particle) {
                    var type = BuiltInRegistries.PARTICLE_TYPE.getKey(particle.particle().getType());
                    particles.add(type + " " + particle.count());
                }
            }

            var effects = new ArrayList<String>();
            for (var packet : userPackets) {
                if (packet instanceof ClientboundUpdateMobEffectPacket effect) {
                    var name = effect.getEffect().unwrapKey().orElseThrow().identifier();
                    effects.add(name + " " + effect.getEffectAmplifier() + " " + effect.getEffectDurationTicks()
                        + (effect.isEffectAmbient() ? " ambient" : "")
                        + (effect.isEffectVisible() ? " visible" : "")
                        + (effect.effectShowsIcon() ? " icon" : ""));
                }
            }

            var changed = false;
            changed |= add(checkpoint, "sounds", sounds);
            changed |= add(checkpoint, "levelEvents", levelEvents);
            changed |= add(checkpoint, "blockEvents", blockEvents);
            changed |= add(checkpoint, "particles", particles);
            changed |= add(checkpoint, "gameEvents", new ArrayList<>(gameEvents));
            changed |= add(checkpoint, "effects", effects);
            return changed;
        }

        // The entities around the target other than players, in a fixed order: "type x y z" relative to the target,
        // to a thousandth of a block, with a living entity's health.
        private JsonArray entities() {
            var found = new ArrayList<String>();
            for (var entity : level.getEntities((Entity) null, new AABB(target).inflate(EntityReach),
                entity -> !(entity instanceof Player))) {
                var type = entity instanceof ItemEntity item
                    ? "item " + BuiltInRegistries.ITEM.getKey(item.getItem().getItem())
                    : BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString();
                var text = type + " " + relative(entity.getX() - target.getX()) + " "
                    + relative(entity.getY() - target.getY()) + " " + relative(entity.getZ() - target.getZ());
                if (entity instanceof LivingEntity living)
                    text += " health " + String.format(Locale.ROOT, "%.1f", living.getHealth());

                found.add(text);
            }

            found.sort(String::compareTo);
            var array = new JsonArray();
            found.forEach(array::add);
            return array;
        }

        private String userPosition() {
            return relative(user.getX() - target.getX()) + " " + relative(user.getY() - target.getY()) + " "
                + relative(user.getZ() - target.getZ());
        }

        // The client data of the block entity at the target, keys sorted, an entity's UUID written as its type.
        private String blockEntityData() {
            var blockEntity = level.getBlockEntity(target);
            if (blockEntity == null)
                return "";

            var tag = blockEntity.getUpdateTag(level.registryAccess());
            var keys = new ArrayList<>(tag.keySet());
            keys.sort(String::compareTo);
            var parts = new ArrayList<String>();
            for (var key : keys)
                parts.add(key + "=" + value(tag.get(key)));

            return String.join(" ", parts);
        }

        private String value(Tag tag) {
            if (tag instanceof IntArrayTag array && array.size() == 4) {
                var values = array.getAsIntArray();
                var uuid = new UUID((long) values[0] << 32 | values[1] & 0xFFFFFFFFL,
                    (long) values[2] << 32 | values[3] & 0xFFFFFFFFL);
                var entity = level.getEntity(uuid);
                return entity == null ? "unknown" : BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString();
            }

            if (tag instanceof CompoundTag compound) {
                var keys = new ArrayList<>(compound.keySet());
                keys.sort(String::compareTo);
                var parts = new ArrayList<String>();
                for (var key : keys)
                    parts.add(key + ":" + value(compound.get(key)));

                return "{" + String.join(",", parts) + "}";
            }

            return tag.toString();
        }

        // The sounds, level events and block events sent to the acting player, in order, as "kind detail".
        private JsonArray userEffects() throws Exception {
            var effects = new JsonArray();
            for (var packet : userPackets) {
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
        private ServerPlayer probePlayer(String name, List<Packet<?>> packets) throws Exception {
            var profile = new com.mojang.authlib.GameProfile(
                UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
            var player = (ServerPlayer) construct("ProbePlayer", server, level, profile);
            player.connection = (ServerGamePacketListenerImpl) call("recorder", packets);

            // The level's chunk tracking marks the chunks a player of it should be sent.
            var chunkSender = ServerGamePacketListenerImpl.class.getDeclaredField("chunkSender");
            chunkSender.setAccessible(true);
            chunkSender.set(player.connection, new net.minecraft.server.network.PlayerChunkSender(false));

            // A teleport moves the connection's player.
            player.connection.player = player;
            return player;
        }
    }

    private static String relative(double value) {
        var text = String.format(Locale.ROOT, "%.3f", value);
        return text.equals("-0.000") ? "0.000" : text;
    }

    // Adds the values under the name when there are any; whether it did.
    private static boolean add(JsonObject checkpoint, String name, List<String> values) {
        if (values.isEmpty())
            return false;

        var array = new JsonArray();
        values.forEach(array::add);
        checkpoint.add(name, array);
        return true;
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
