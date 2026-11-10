import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import com.mojang.serialization.JsonOps;
import io.netty.channel.ChannelFutureListener;
import java.lang.reflect.Field;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.SectionPos;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.NbtOps;
import net.minecraft.network.chat.Component;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundSwingAnimationPacket;
import net.minecraft.network.protocol.game.ClientboundSetEquipmentPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.network.protocol.game.ClientboundSoundEntityPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ChunkMap;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.network.ServerPlayerConnection;
import net.minecraft.server.players.PlayerList;
import net.minecraft.stats.Stat;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.decoration.ArmorStand;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.gameevent.BlockPositionSource;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gameevent.PositionSource;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Records a player using an armor stand (vanilla's {@code ArmorStand.interact} at the clicked point), on a headless
 * dedicated server in a flat world of air, through what the server runs for an interact packet
 * ({@code ServerGamePacketListenerImpl.handleInteract}): the player's {@code interactOn}, then for a success the
 * {@code player_interacted_with_entity} trigger and the arm's swing.
 *
 * <p>Each case of the fixture is its input: the stand's flags (small, arms, marker, base plate, disabled slots) and
 * what it wears and holds, the point clicked on it (relative to its position), the item and count in the user's
 * hand, which hand, the game mode and whether the item has a custom name. The probe records the result, the user's
 * hand after it, the stand's equipment, the stand's saved data, the sounds and swings a second player standing by
 * received, the equipment packets that player received once the stand ticked, the game events, the statistics and
 * the advancement criteria completed.
 */
public final class ArmorStandFixtures {
    private static final int Reach = 3;

    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // The saved fields compared: the stand's own and its equipment.
    private static final String[] SavedFields =
        { "equipment", "DisabledSlots", "ShowArms", "Small", "NoBasePlate", "Marker", "Pose", "Invisible" };

    private static MinecraftServer server;

    private static BlockPos origin;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();

        try {
            startServer();
            forceArea(root);
            server.submit(() -> {
                try {
                    var selected = args.length > 1 && !args[1].isBlank()
                        ? Set.of(args[1].split("\\+")) : Set.<String>of();
                    capture(root, selected);
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
        Files.writeString(fixturePath, compact(gson.toJson(root)) + "\n", StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();
        Runtime.getRuntime().halt(0);
    }

    private static void forceArea(JsonObject root) throws Exception {
        var target = position(root.getAsJsonArray("target"));
        var level = server.overworld();
        server.submit(() -> level.setChunkForced(target.getX() >> 4, target.getZ() >> 4, true)).join();

        var chunk = net.minecraft.world.level.ChunkPos.containing(target).pack();
        while (!server.submit(() -> level.shouldTickBlocksAt(chunk) && level.areEntitiesLoaded(chunk)).join())
            Thread.sleep(50);
    }

    private static void capture(JsonObject root, Set<String> selected) throws Exception {
        var level = server.overworld();
        origin = position(root.getAsJsonArray("target"));

        var user = new ProbePlayer(server, level, profile("User"));
        var userPackets = new ArrayList<Packet<?>>();
        user.connection = recorder(userPackets);
        user.connection.player = user;
        players(server.getPlayerList()).add(user);

        var bystander = new ProbePlayer(server, level, profile("Bystander"));
        var heard = new ArrayList<Packet<?>>();
        bystander.connection = recorder(heard);
        bystander.connection.player = bystander;
        bystander.snapTo(origin.getX() + 0.5, origin.getY() + 3, origin.getZ() + 0.5, 0, 0);
        players(server.getPlayerList()).add(bystander);
        trackUser(level, user, bystander);

        var gameEvents = new ArrayList<String>();
        var listener = new Listener(origin, user, gameEvents);
        level.getChunkAt(origin).getListenerRegistry(SectionPos.blockToSectionCoord(origin.getY())).register(listener);

        // The stand stands on stone.
        level.setBlock(origin.below(), Blocks.STONE.defaultBlockState(), QuietFlags);

        for (var element : root.getAsJsonArray("cases")) {
            var input = element.getAsJsonObject();
            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            discardEntities(level);
            var stand = stand(level, input);
            bystanderSees(level, stand, bystander);
            prepare(user, root, input);
            var advancement = prepareCriteria(level, user, input);

            userPackets.clear();
            heard.clear();
            gameEvents.clear();
            user.awarded.clear();
            finishSwing(user);

            var hand = hand(input);
            var clicked = input.getAsJsonArray("clicked");
            var location = new Vec3(clicked.get(0).getAsDouble(), clicked.get(1).getAsDouble(),
                clicked.get(2).getAsDouble());
            var result = interact(user, stand, hand, location);

            // The stand's tick sends what changed of its equipment to the players who see it.
            level.tickNonPassenger(stand);

            input.addProperty("result", result(result));
            input.addProperty("hand", stack(user.getItemInHand(hand)));
            input.add("equipment", equipment(stand));
            input.add("saved", saved(level, stand));
            record(input, heard, userPackets, gameEvents);
            input.add("stats", strings(user.awarded));
            input.add("completedCriteria", criteria(user, advancement));
        }

        discardEntities(level);
        players(server.getPlayerList()).remove(user);
        players(server.getPlayerList()).remove(bystander);
        System.out.println("Interacted with " + root.getAsJsonArray("cases").size() + " stands");
    }

    // What ServerGamePacketListenerImpl.handleInteract does once the target is in range: the interaction, then for
    // a success the trigger, with the item as it was when the interaction used it, and the swing.
    private static InteractionResult interact(ProbePlayer user, ArmorStand stand, InteractionHand hand,
        Vec3 location) {
        var used = user.getItemInHand(hand).copy();
        var result = user.interactOn(stand, hand, location);
        if (result instanceof InteractionResult.Success success) {
            var awarded = success.wasItemInteraction() ? used : ItemStack.EMPTY;
            net.minecraft.advancements.triggers.CriteriaTriggers.PLAYER_INTERACTED_WITH_ENTITY.trigger(user, awarded, stand);
            if (success.shouldSwing()) {
                user.swingAndResetAttackStrength(hand, used.getInteractAnimation(),
                    success.swingSource() != InteractionResult.SwingSource.PREDICTED);
            }
        }

        return result;
    }

    // A stand at the target, with the case's flags and equipment.
    private static ArmorStand stand(ServerLevel level, JsonObject input) throws Exception {
        var stand = new ArmorStand(level, origin.getX() + 0.5, origin.getY(), origin.getZ() + 0.5);
        stand.setShowArms(flag(input, "showArms"));
        stand.setNoBasePlate(flag(input, "noBasePlate"));
        invoke(stand, "setSmall", flag(input, "small"));
        invoke(stand, "setMarker", flag(input, "marker"));
        var disabled = ArmorStand.class.getDeclaredField("disabledSlots");
        disabled.setAccessible(true);
        disabled.setInt(stand, input.has("disabledSlots") ? input.get("disabledSlots").getAsInt() : 0);

        if (input.has("wearing")) {
            for (var entry : input.getAsJsonObject("wearing").entrySet()) {
                var slot = EquipmentSlot.byName(entry.getKey());
                stand.setItemSlot(slot, item(entry.getValue().getAsString(), 1, null));
            }
        }

        level.addFreshEntity(stand);

        // The equipment it starts with is already what the players see.
        level.tickNonPassenger(stand);
        return stand;
    }

    private static void invoke(ArmorStand stand, String name, boolean value) throws Exception {
        var method = ArmorStand.class.getDeclaredMethod(name, boolean.class);
        method.setAccessible(true);
        method.invoke(stand, value);
    }

    // The last case's swing has finished, as it would have by the next click.
    private static void finishSwing(ProbePlayer user) throws Exception {
        var field = net.minecraft.world.entity.LivingEntity.class.getDeclaredField("swingState");
        field.setAccessible(true);
        field.set(user, new net.minecraft.world.entity.LivingEntity.SwingState());
    }

    private static boolean flag(JsonObject input, String name) {
        return input.has(name) && input.get(name).getAsBoolean();
    }

    private static void prepare(ProbePlayer user, JsonObject root, JsonObject input) {
        var standing = root.getAsJsonArray("player");
        var mode = input.has("gameMode") ? input.get("gameMode").getAsString() : "survival";
        user.setGameMode(GameType.byName(mode));
        user.snapTo(standing.get(0).getAsDouble(), standing.get(1).getAsDouble(), standing.get(2).getAsDouble(),
            standing.get(3).getAsFloat(), standing.get(4).getAsFloat());
        user.setShiftKeyDown(false);
        user.getInventory().clearContent();
        user.getInventory().setSelectedSlot(0);

        var name = input.has("itemName") ? input.get("itemName").getAsString() : null;
        var held = input.has("item")
            ? item(input.get("item").getAsString(), input.has("count") ? input.get("count").getAsInt() : 1, name)
            : ItemStack.EMPTY;
        user.setItemInHand(hand(input), held);
    }

    private static ItemStack item(String id, int count, String name) {
        var item = BuiltInRegistries.ITEM.getValue(Identifier.parse(id));
        var stack = new ItemStack(item, count);
        if (name != null)
            stack.set(DataComponents.CUSTOM_NAME, Component.literal(name));

        return stack;
    }

    private static InteractionHand hand(JsonObject input) {
        return input.has("handUsed") && input.get("handUsed").getAsString().equals("offhand")
            ? InteractionHand.OFF_HAND : InteractionHand.MAIN_HAND;
    }

    private static net.minecraft.advancements.AdvancementHolder prepareCriteria(
        ServerLevel level, ProbePlayer user, JsonObject input) throws Exception {
        if (!input.has("criteria"))
            return null;

        var definition = new JsonObject();
        definition.add("criteria", input.getAsJsonObject("criteria").deepCopy());
        var ops = server.reloadableRegistries().lookup().createSerializationContext(JsonOps.INSTANCE);
        var value = net.minecraft.advancements.Advancement.CODEC.parse(ops, definition).getOrThrow();
        var id = Identifier.parse("probe:" + input.get("name").getAsString());
        var holder = new net.minecraft.advancements.AdvancementHolder(id, value);
        user.getAdvancements().getOrStartProgress(holder);
        var register = net.minecraft.server.PlayerAdvancements.class.getDeclaredMethod(
            "registerListeners", net.minecraft.advancements.AdvancementHolder.class);
        register.setAccessible(true);
        register.invoke(user.getAdvancements(), holder);
        return holder;
    }

    private static JsonArray criteria(ProbePlayer user, net.minecraft.advancements.AdvancementHolder holder) {
        var done = new ArrayList<String>();
        if (holder != null) {
            var progress = user.getAdvancements().getOrStartProgress(holder);
            for (var name : holder.value().criteria().keySet()) {
                if (progress.getCriterion(name).isDone())
                    done.add(name);
            }
        }

        done.sort(String::compareTo);
        return strings(done);
    }

    // Each slot the stand fills, as "<slot>=<item>", in vanilla's slot order.
    private static JsonArray equipment(ArmorStand stand) {
        var filled = new ArrayList<String>();
        for (var slot : EquipmentSlot.values()) {
            var item = stand.getItemBySlot(slot);
            if (!item.isEmpty())
                filled.add(slot.getName() + "=" + stack(item));
        }

        return strings(filled);
    }

    private static JsonObject saved(ServerLevel level, ArmorStand stand) {
        var output = net.minecraft.world.level.storage.TagValueOutput.createWithContext(
            net.minecraft.util.ProblemReporter.DISCARDING, level.registryAccess());
        stand.saveWithoutId(output);
        CompoundTag tag = output.buildResult();
        var kept = new CompoundTag();
        for (var name : SavedFields) {
            if (tag.contains(name))
                kept.put(name, tag.get(name));
        }

        return NbtOps.INSTANCE.convertTo(JsonOps.INSTANCE, kept).getAsJsonObject();
    }

    // The sounds, swings and equipment packets the bystander received, and the game events.
    private static void record(JsonObject output, List<Packet<?>> heard, List<Packet<?>> userPackets,
        List<String> gameEvents) {
        var sounds = new ArrayList<String>();
        var swings = new ArrayList<String>();
        var equipment = new ArrayList<String>();
        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                sounds.add(sound.getSound().value().location() + " " + sound.getSource().getName() + " "
                    + (sound.getX() - origin.getX()) + "," + (sound.getY() - origin.getY()) + ","
                    + (sound.getZ() - origin.getZ()) + " " + sound.getVolume() + " " + sound.getPitch());
            } else if (packet instanceof ClientboundSoundEntityPacket sound) {
                sounds.add(sound.getSound().value().location() + " " + sound.getSource().getName() + " entity "
                    + sound.getVolume() + " " + sound.getPitch());
            } else if (packet instanceof ClientboundSwingAnimationPacket swing) {
                swings.add("bystander " + swing.hand().name().toLowerCase(java.util.Locale.ROOT));
            } else if (packet instanceof ClientboundSetEquipmentPacket set) {
                for (var pair : set.getSlots())
                    equipment.add(pair.getFirst().getName() + "=" + stack(pair.getSecond()));
            }
        }

        for (var packet : userPackets) {
            if (packet instanceof ClientboundSwingAnimationPacket swing)
                swings.add("user " + swing.hand().name().toLowerCase(java.util.Locale.ROOT));
        }

        output.add("sounds", strings(sounds));
        output.add("swings", strings(swings));
        output.add("equipmentPackets", strings(equipment));
        output.add("gameEvents", strings(new ArrayList<>(gameEvents)));
    }

    private static void discardEntities(ServerLevel level) {
        for (var entity : level.getEntities((Entity) null, new AABB(origin).inflate(Reach),
            entity -> !(entity instanceof ServerPlayer))) {
            entity.discard();
        }
    }

    // Tracks the user's entity, with the bystander seeing it, so its swings reach the bystander.
    private static void trackUser(ServerLevel level, ProbePlayer user, ProbePlayer bystander) throws Exception {
        var chunkMap = level.getChunkSource().chunkMap;
        var trackedClass = Class.forName("net.minecraft.server.level.ChunkMap$TrackedEntity");
        var constructor = trackedClass.getDeclaredConstructors()[0];
        constructor.setAccessible(true);
        var interval = constructor.getParameterTypes()[3];
        var never = interval.getField("NEVER").get(null);
        var tracked = constructor.newInstance(chunkMap, user, 160, never, true);
        seenBy(tracked).add(bystander.connection);
        entityMap(chunkMap).put(user.getId(), tracked);
    }

    // The bystander sees the stand, as a player in range would.
    private static void bystanderSees(ServerLevel level, ArmorStand stand, ProbePlayer bystander) throws Exception {
        var tracked = entityMap(level.getChunkSource().chunkMap).get(stand.getId());
        seenBy(tracked).add(bystander.connection);
    }

    @SuppressWarnings("unchecked")
    private static Set<ServerPlayerConnection> seenBy(Object tracked) throws Exception {
        var field = tracked.getClass().getDeclaredField("seenBy");
        field.setAccessible(true);
        return (Set<ServerPlayerConnection>) field.get(tracked);
    }

    @SuppressWarnings("unchecked")
    private static it.unimi.dsi.fastutil.ints.Int2ObjectMap<Object> entityMap(ChunkMap chunkMap) throws Exception {
        var field = ChunkMap.class.getDeclaredField("entityMap");
        field.setAccessible(true);
        return (it.unimi.dsi.fastutil.ints.Int2ObjectMap<Object>) field.get(chunkMap);
    }

    private static GameProfile profile(String name) {
        return new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
    }

    private static String result(InteractionResult result) {
        if (result instanceof InteractionResult.Success success) {
            return switch (success.swingSource()) {
                case PREDICTED -> "success";
                case SERVER_ONLY -> "success_server";
                case NONE -> "consume";
            };
        }

        if (result instanceof InteractionResult.Fail)
            return "fail";

        return "pass";
    }

    // An item stack as "<id> x<count>", with its custom name when it has one, or "empty".
    private static String stack(ItemStack stack) {
        if (stack.isEmpty())
            return "empty";

        var text = BuiltInRegistries.ITEM.getKey(stack.getItem()) + " x" + stack.getCount();
        var name = stack.get(DataComponents.CUSTOM_NAME);
        return name == null ? text : text + " named=" + name.getString();
    }

    @SuppressWarnings("unchecked")
    private static List<ServerPlayer> players(PlayerList list) throws Exception {
        var field = PlayerList.class.getDeclaredField("players");
        field.setAccessible(true);
        return (List<ServerPlayer>) field.get(list);
    }

    private static ServerGamePacketListenerImpl recorder(List<Packet<?>> packets) throws Exception {
        var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
        var recorder = (Recorder) allocate.invoke(unsafe, Recorder.class);
        recorder.packets = packets;
        return recorder;
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static JsonArray strings(List<String> values) {
        var array = new JsonArray();
        for (var value : values)
            array.add(value);

        return array;
    }

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

    /** A player whose statistics are recorded. */
    private static final class ProbePlayer extends ServerPlayer {
        final List<String> awarded = new ArrayList<>();

        ProbePlayer(MinecraftServer server, ServerLevel level, GameProfile profile) {
            super(server, level, profile, ClientInformation.createDefault());
        }

        @Override public void awardStat(Stat<?> stat, int amount) {
            awarded.add(statName(stat) + " " + amount);
            super.awardStat(stat, amount);
        }

        private static <T> String statName(Stat<T> stat) {
            var type = BuiltInRegistries.STAT_TYPE.getKey(stat.getType());
            var value = stat.getType().getRegistry().getKey(stat.getValue());
            return type + " " + value;
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
    }

    /** Records every game event within reach of the target, by name, offset and source. */
    private static final class Listener implements GameEventListener {
        private final BlockPos target;
        private final Entity user;
        private final List<String> events;

        Listener(BlockPos target, Entity user, List<String> events) {
            this.target = target;
            this.user = user;
            this.events = events;
        }

        @Override public PositionSource getListenerSource() {
            return new BlockPositionSource(target);
        }

        @Override public int getListenerRadius() {
            return 16;
        }

        @Override public boolean handleGameEvent(ServerLevel level, Holder<GameEvent> event, GameEvent.Context context,
            Vec3 pos) {
            var name = event.unwrapKey().orElseThrow().identifier().toString();
            var block = BlockPos.containing(pos).subtract(target);
            events.add(name + " " + block.getX() + "," + block.getY() + "," + block.getZ() + " "
                + source(context.sourceEntity()));
            return true;
        }

        private String source(Entity entity) {
            if (entity == null)
                return "none";

            if (entity == user)
                return "player";

            return BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString();
        }
    }
}
