import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import io.netty.channel.ChannelFutureListener;
import java.lang.reflect.Field;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.time.LocalDate;
import java.time.Month;
import java.time.MonthDay;
import java.time.temporal.ChronoUnit;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.HashMap;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import java.util.concurrent.atomic.AtomicLong;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.SectionPos;
import net.minecraft.core.UUIDUtil;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.nbt.IntArrayTag;
import net.minecraft.nbt.ListTag;
import net.minecraft.nbt.StringTag;
import net.minecraft.nbt.Tag;
import net.minecraft.nbt.TagParser;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundBlockEntityDataPacket;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelParticlesPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ChunkTrackingView;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.PlayerMap;
import net.minecraft.server.level.ServerChunkCache;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.players.PlayerList;
import net.minecraft.util.ProblemReporter;
import net.minecraft.util.SpecialDates;
import net.minecraft.util.profiling.Profiler;
import net.minecraft.util.profiling.ProfilerFiller;
import net.minecraft.world.Difficulty;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.OminousItemSpawner;
import net.minecraft.world.entity.EntityType;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.ChunkPos;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.BlockEntity;
import net.minecraft.world.level.block.entity.TrialSpawnerBlockEntity;
import net.minecraft.world.level.block.entity.vault.VaultBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gameevent.BlockPositionSource;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gameevent.PositionSource;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.storage.TagValueInput;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;

/**
 * Records trial spawners on a headless dedicated server in a flat world of air, one game tick at a time, and the trial
 * spawners and vaults a placed trial chamber holds.
 *
 * <p>A {@code trial} case is its input: the blocks set up around its origin, the spawner's state and saved data (set as
 * a structure sets them), the players standing around it, the seed of the level's random, and what happens to each mob
 * the spawner spawns ({@code mobActions}: killed, discarded, or moved out of its tracking range, under the floor, some
 * ticks after it appeared). It steps the frozen game one tick at a time and records after each tick the blocks the
 * level stored (in order, with their flags), the blocks that changed, the spawner's saved data, the block entity data a
 * listener got for it (the chunk's changes are sent at the end of each tick, where vanilla sends them at the start of
 * the next), the entities in the area, the sounds, level events and particles the listener received, the game events
 * within 16 blocks, the state of the level's random, and each item's id modulo 4 (which picks the ticks a resting item
 * moves on, an input the replay takes as ids can't match). Mobs' and the spawner's UUID sets are written in a fixed
 * order: vanilla saves them from a hash set whose order changes between runs. Tracked mobs are named by the order they
 * spawned in, as their UUIDs come from their own unseeded randoms.
 *
 * <p>The captures are taken as on {@link #CapturedOn}, which isn't Halloween: a bare skeleton or zombie draws once
 * more for a pumpkin on Halloween, and the replay sets its clock to the same day.
 *
 * <p>Natural spawning would shuffle chunks with the level's random every tick, so the game rule stays off; the
 * spawner uses its test override (vanilla's own) to spawn anyway, except in the cases that test the rules.
 *
 * <p>A {@code chamber} case places a structure as {@code /place structure} does and records the saved data of every
 * trial spawner and vault in it, as placed and after a few ticks in which its spawners may spawn (with no players
 * around, they only pick their next spawn data from their registry configs).
 */
public final class TrialSpawnerFixtures {
    // Set without neighbor or shape updates and without drops, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // How far from the origin the area that is cleared, watched and compared reaches.
    private static final int Reach = 8;
    private static final int Below = 2;
    private static final int Above = 6;

    // The cases run Spacing blocks apart along x, each in a chunk of its own.
    private static final int Spacing = 32;

    // Ticks run after a case's area is cleared, so that ticks it left scheduled find air.
    private static final int DrainTicks = 3;

    // How far a mob that leaves is moved down: beyond the 47 blocks a spawner tracks its mobs to, and under the
    // floor, out of the sun, so that a mob with its AI never burns there by its own random.
    private static final int LeaveDepth = 60;

    // The day vanilla's SpecialDates take today to be, which the replay's clock reads too.
    private static final LocalDate CapturedOn = LocalDate.of(2026, 6, 1);

    // The listener floats this far above the origin: a spectator, which spawners never detect.
    private static final Vec3 ListenerOffset = new Vec3(0.5, 10, 0.5);

    private static MinecraftServer server;
    private static ServerLevel level;
    private static Thread serverThread;
    private static BlockPos origin;
    private static boolean ominousCapture;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();
        ominousCapture = root.has("ominous") && root.get("ominous").getAsBoolean();
        var selected = args.length > 1 && !args[1].isBlank() ? Set.of(args[1].split("\\+")) : Set.<String>of();

        try {
            runOn(CapturedOn);
            installLootTables(root);
            startServer();
            level = server.overworld();
            serverThread = server.submit(Thread::currentThread).join();
            var cases = root.getAsJsonArray("cases");
            server.submit(() -> configure(cases)).join();
            waitUntilTicking(cases);

            var captured = 0;
            for (int index = 0; index < cases.size(); index++) {
                var input = cases.get(index).getAsJsonObject();
                if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                    continue;

                if (isChamber(input))
                    captureChamber(input);
                else
                    captureTrial(input, index);

                captured++;
                System.out.println("Captured " + input.get("name").getAsString());
            }

            if (!selected.isEmpty() && captured != selected.size())
                throw new IllegalArgumentException("Every selected case must exist and be named once.");
        } catch (Throwable e) {
            e.printStackTrace();
            System.out.flush();
            Runtime.getRuntime().halt(1);
        }

        root.addProperty("version", server.getServerVersion());
        if (ominousCapture)
            root.add("releaseMotion", server.submit(TrialSpawnerFixtures::releaseMotion).join());
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(fixturePath, (compact(gson.toJson(root)) + "\n").replace("\n", "\r\n"),
            StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    private static boolean isChamber(JsonObject input) {
        return input.has("kind") && input.get("kind").getAsString().equals("chamber");
    }

    // Nothing moves unless stepped: the game is frozen, and time, weather, random ticks and natural spawning are off.
    // Each trial case's chunk is forced, so its blocks and entities tick when the game steps.
    private static void configure(JsonArray cases) {
        server.tickRateManager().setFrozen(true);
        var rules = server.getGameRules();
        rules.set(GameRules.ADVANCE_TIME, false, server);
        rules.set(GameRules.ADVANCE_WEATHER, false, server);
        rules.set(GameRules.SPAWN_MOBS, false, server);
        rules.set(GameRules.MOB_DROPS, false, server);
        rules.set(GameRules.RANDOM_TICK_SPEED, 0, server);
        for (int index = 0; index < cases.size(); index++) {
            if (isChamber(cases.get(index).getAsJsonObject()))
                continue;

            var at = originOf(index);
            level.setChunkForced(at.getX() >> 4, at.getZ() >> 4, true);
        }
    }

    private static void waitUntilTicking(JsonArray cases) throws Exception {
        for (int index = 0; index < cases.size(); index++) {
            if (isChamber(cases.get(index).getAsJsonObject()))
                continue;

            waitUntilTicking(originOf(index));
        }
    }

    private static void waitUntilTicking(BlockPos at) throws Exception {
        var chunk = ChunkPos.containing(at).pack();
        while (!server.submit(() -> level.shouldTickBlocksAt(chunk)
            && level.areEntitiesLoaded(chunk)
            && level.isPositionEntityTicking(at)).join()) {
            Thread.sleep(50);
        }
    }

    /** Where the case at <paramref name="index"/> runs: the middle of a chunk of its own, at y 64. */
    private static BlockPos originOf(int index) {
        return new BlockPos(index * Spacing + 8, 64, 8);
    }

    private static void captureTrial(JsonObject input, int index) {
        origin = originOf(index);
        server.submit(() -> {
            clear();
            discardEntities();
        }).join();
        step(DrainTicks);

        var heard = new ArrayList<Packet<?>>();
        var gameEvents = new ArrayList<String>();
        var writes = new JsonArray();
        var state = server.submit(() -> prepare(input, index, heard, gameEvents, writes)).join();

        // Each tick is one task on the server thread, so the frozen server's own loop, which still sends chunks'
        // changes, never runs inside it: what the tick changed is sent at its end, then recorded.
        var checkpoints = new JsonArray();
        var ticks = input.get("ticks").getAsInt();
        for (int tick = 1; tick <= ticks; tick++) {
            var current = tick;
            var checkpoint = server.submit(() -> {
                act(input, state, current);
                tickOnce();
                broadcastChanges();
                var result = checkpoint(state, current, heard, gameEvents, writes);
                if (ominousCapture)
                    result.add("ominous", ominousState(state));

                return result;
            }).join();
            checkpoints.add(checkpoint);
        }

        input.add("checkpoints", checkpoints);
        input.addProperty("finalSaved", server.submit(() -> machine().saveWithoutMetadata(level.registryAccess()))
            .join().toString());
        server.submit(() -> {
            System.getProperties().remove("obsidian.blockUse.writes");
            unregister(state);
            clear();
            discardEntities();
        }).join();
    }

    /** What a trial case keeps between its ticks. */
    private record Trial(ProbePlayer listener, List<ProbePlayer> players, Listener gameEventListener,
        List<BlockState> blocks, List<Mob> mobs, Map<Mob, Integer> appeared) {
    }

    private static Trial prepare(JsonObject input, int index, List<Packet<?>> heard, List<String> gameEvents,
        JsonArray writes) {
        applyRules(input);
        setup(input);
        if (input.has("vault"))
            placeVault(input);
        else
            placeSpawner(input);

        // What the setup left to send goes out now, before anyone listens.
        broadcastChanges();

        var players = new ArrayList<ProbePlayer>();
        if (input.has("players")) {
            for (var element : input.getAsJsonArray("players")) {
                var spec = element.getAsJsonObject();
                var name = spec.get("name").getAsString();
                var player = new ProbePlayer(server, level, UUIDUtil.createOfflineProfile(name));
                player.connection = recorder(player, new ArrayList<>());
                var mode = spec.has("gameMode") ? spec.get("gameMode").getAsString() : "survival";
                player.setGameMode(GameType.byName(mode));
                var at = Vec3.atLowerCornerOf(origin).add(vector(spec.getAsJsonArray("pos")));
                player.snapTo(at.x, at.y, at.z, 0, 0);
                players(server.getPlayerList()).add(player);
                level.players().add(player);
                spec.addProperty("uuid", player.getUUID().toString());
                players.add(player);
            }
        }

        // A spectator of its own for each case, which hears the area and gets its chunk's block entity data.
        var listener = new ProbePlayer(server, level, profile("Listener" + index));
        listener.connection = recorder(listener, heard);
        listener.setGameMode(GameType.SPECTATOR);
        var at = Vec3.atLowerCornerOf(origin).add(ListenerOffset);
        listener.snapTo(at.x, at.y, at.z, 0, 0);
        players(server.getPlayerList()).add(listener);
        level.players().add(listener);
        playerMap().addPlayer(listener, false);
        listener.setChunkTrackingView(ChunkTrackingView.of(ChunkPos.containing(origin), 1));

        var gameEventListener = new Listener(origin, gameEvents);
        level.getChunkAt(origin).getListenerRegistry(SectionPos.blockToSectionCoord(origin.getY()))
            .register(gameEventListener);

        input.addProperty("startTime", level.getGameTime());
        level.getRandom().setSeed(input.get("seed").getAsLong());
        if (input.has("vault") || ominousCapture)
            seedSounds(input.get("seed").getAsLong());

        observeWrites(writes);

        return new Trial(listener, players, gameEventListener, snapshot(), new ArrayList<>(), new HashMap<>());
    }

    // Packet sound seeds use an independent random. Seeding it makes packet comparison repeatable without
    // changing any of the vault's gameplay draws (display loot, reward sequences or ejection motion).
    private static void seedSounds(long seed) {
        try {
            var field = net.minecraft.world.level.Level.class.getDeclaredField("soundSeedGenerator");
            field.setAccessible(true);
            ((net.minecraft.util.RandomSource) field.get(level)).setSeed(seed);
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    // The case's difficulty (normal by default), and whether spawner blocks work (they do by default).
    private static void applyRules(JsonObject input) {
        var difficulty = input.has("difficulty") ? input.get("difficulty").getAsString() : "normal";
        server.setDifficulty(Difficulty.byName(difficulty), true);
        var spawnersWork = !input.has("spawnerBlocksWork") || input.get("spawnerBlocksWork").getAsBoolean();
        server.getGameRules().set(GameRules.SPAWNER_BLOCKS_WORK, spawnersWork, server);
    }

    // The case's blocks, set without updates, single positions or filled boxes, in order.
    private static void setup(JsonObject input) {
        if (!input.has("setup"))
            return;

        for (var element : input.getAsJsonArray("setup")) {
            var placement = element.getAsJsonObject();
            var block = state(placement.get("state").getAsString());
            var from = origin.offset(position(placement.getAsJsonArray("pos")));
            var to = placement.has("to") ? origin.offset(position(placement.getAsJsonArray("to"))) : from;
            for (var pos : BlockPos.betweenClosed(from, to))
                level.setBlock(pos, block, QuietFlags);
        }
    }

    // The spawner at the origin, set without updates and loaded with the case's data as a structure loads it, and
    // allowed to spawn with natural spawning off unless the case tests that rule.
    private static void placeSpawner(JsonObject input) {
        var spec = input.getAsJsonObject("spawner");
        level.setBlock(origin, state(spec.get("state").getAsString()), QuietFlags);
        var tag = parse(spec.get("nbt").getAsString());
        var spawner = spawner();
        spawner.loadWithComponents(TagValueInput.create(ProblemReporter.DISCARDING, level.registryAccess(), tag));
        spawner.setChanged();

        var override = !input.has("overrideSpawnRules") || input.get("overrideSpawnRules").getAsBoolean();
        if (override)
            spawner.getTrialSpawner().overridePeacefulAndMobSpawnRule();
    }

    private static TrialSpawnerBlockEntity spawner() {
        return (TrialSpawnerBlockEntity) level.getBlockEntity(origin);
    }

    private static BlockEntity machine() {
        return level.getBlockEntity(origin);
    }

    private static void placeVault(JsonObject input) {
        var spec = input.getAsJsonObject("vault");
        level.setBlock(origin, state(spec.get("state").getAsString()), QuietFlags);
        var tag = parse(spec.get("nbt").getAsString());
        if (input.has("rewardedCount")) {
            var rewarded = new ListTag();
            for (int index = 0; index < input.get("rewardedCount").getAsInt(); index++) {
                var uuid = UUIDUtil.createOfflineProfile("Old" + index).id();
                rewarded.add(new IntArrayTag(UUIDUtil.uuidToIntArray(uuid)));
            }

            var data = tag.getCompoundOrEmpty("server_data");
            data.put("rewarded_players", rewarded);
            tag.put("server_data", data);
        }

        spec.addProperty("nbt", tag.toString());
        machine().loadWithComponents(TagValueInput.create(ProblemReporter.DISCARDING, level.registryAccess(), tag));
        machine().setChanged();
    }

    // What happens before the tick: the case's events, then what each mob does once it has been around long enough.
    private static void act(JsonObject input, Trial state, int tick) {
        if (ominousCapture) {
            // Entity constructors use unseeded private randoms. Observe releases at birth, then discard them before
            // flight: the separate seeded Projectile.getMovementToShoot probes check release motion exactly.
            for (var entity : level.getEntities((Entity) null, chunkBox(), entity ->
                !(entity instanceof Player) && !(entity instanceof Mob) && !(entity instanceof OminousItemSpawner))) {
                entity.discard();
            }
        }

        if (input.has("events")) {
            for (var element : input.getAsJsonArray("events")) {
                var event = element.getAsJsonObject();
                if (event.get("tick").getAsInt() != tick)
                    continue;

                if (event.has("spawn")) {
                    var tag = parse(event.get("spawn").getAsString());
                    var entity = EntityType.loadEntityRecursive(EntityTypes.OMINOUS_ITEM_SPAWNER, tag, level,
                        EntitySpawnReason.COMMAND, value -> value);
                    var at = Vec3.atLowerCornerOf(origin).add(vector(event.getAsJsonArray("pos")));
                    entity.snapTo(at);
                    level.addFreshEntity(entity);
                    continue;
                }

                var player = player(state, event.get("player").getAsString());
                if (event.has("effect")) {
                    var effect = event.getAsJsonObject("effect");
                    var holder = effect.get("id").getAsString().equals("bad_omen")
                        ? MobEffects.BAD_OMEN : MobEffects.TRIAL_OMEN;
                    player.addEffect(new MobEffectInstance(holder, effect.get("duration").getAsInt(),
                        effect.get("amplifier").getAsInt()));
                }
                if (event.has("pos")) {
                    var at = Vec3.atLowerCornerOf(origin).add(vector(event.getAsJsonArray("pos")));
                    player.snapTo(at.x, at.y, at.z, 0, 0);
                }
                if (event.has("gameMode"))
                    player.setGameMode(GameType.byName(event.get("gameMode").getAsString()));

                if (event.has("use")) {
                    var item = ItemStack.CODEC.parse(level.registryAccess().createSerializationContext(
                        net.minecraft.nbt.NbtOps.INSTANCE), parse(event.get("use").getAsString())).getOrThrow();
                    player.setItemInHand(InteractionHand.MAIN_HAND, item);
                    var hit = new BlockHitResult(Vec3.atCenterOf(origin), Direction.NORTH, origin, false);
                    var result = player.gameMode.useItemOn(player, level, item, InteractionHand.MAIN_HAND, hit);
                    var resultTag = new JsonObject();
                    resultTag.addProperty("consumesAction", result.consumesAction());
                    var encoded = ItemStack.CODEC.encodeStart(level.registryAccess().createSerializationContext(
                        net.minecraft.nbt.NbtOps.INSTANCE), item);
                    resultTag.addProperty("hand", item.isEmpty() ? "{}" : encoded.getOrThrow().toString());
                    event.add("output", resultTag);
                }
            }
        }

        if (!input.has("mobActions"))
            return;

        var actions = input.getAsJsonArray("mobActions");
        for (int ordinal = 0; ordinal < state.mobs.size(); ordinal++) {
            var mob = state.mobs.get(ordinal);
            var action = actions.get(ordinal % actions.size()).getAsJsonObject();
            if (state.appeared.get(mob) + action.get("after").getAsInt() != tick || !mob.isAlive())
                continue;

            var kind = action.get("action").getAsString();
            if (kind.equals("kill")) {
                mob.kill(level);
            } else if (kind.equals("discard")) {
                mob.discard();
            } else {
                mob.setNoGravity(true);
                mob.teleportTo(mob.getX(), mob.getY() - LeaveDepth, mob.getZ());
            }
        }
    }

    private static ProbePlayer player(Trial state, String name) {
        for (var player : state.players) {
            if (player.getGameProfile().name().equals(name))
                return player;
        }

        throw new IllegalArgumentException("No player " + name);
    }

    private static JsonObject checkpoint(Trial state, int tick, List<Packet<?>> heard, List<String> gameEvents,
        JsonArray writes) {
        // The mobs that appeared this tick, in the order they were added.
        var found = new ArrayList<Entity>(
            level.getEntities((Entity) null, chunkBox(), entity -> entity instanceof Mob));
        found.sort(Comparator.comparingInt(Entity::getId));
        for (var entity : found) {
            var mob = (Mob) entity;
            if (!state.appeared.containsKey(mob)) {
                state.mobs.add(mob);
                state.appeared.put(mob, tick);
                if (ominousCapture)
                    mob.setSilent(true);
            }
        }

        var checkpoint = new JsonObject();
        checkpoint.add("writes", writes.deepCopy());
        while (!writes.isEmpty())
            writes.remove(0);

        checkpoint.add("changes", changes(state.blocks));
        checkpoint.addProperty("saved", normalize(machine().saveWithoutMetadata(level.registryAccess()), state));
        checkpoint.add("client", client(heard, state));
        checkpoint.add("entities", entities(state));
        checkpoint.add("itemPhases", itemPhases());
        if (machine() instanceof VaultBlockEntity) {
            checkpoint.addProperty("updateTag", machine().getUpdateTag(level.registryAccess()).toString());
            checkpoint.add("itemMotion", itemMotion());
            checkpoint.add("itemStacks", itemStacks());
        }
        recordEvents(checkpoint, heard, gameEvents);
        checkpoint.addProperty("random", randomState());
        heard.clear();
        gameEvents.clear();
        return checkpoint;
    }

    // The spawner's saved data as SNBT, with its UUID sets in a fixed order and its mobs by the order they spawned in.
    private static String normalize(CompoundTag saved, Trial state) {
        var tag = saved.copy();
        if (tag.get("registered_players") instanceof ListTag players) {
            var sorted = new ArrayList<UUID>();
            for (var element : players)
                sorted.add(UUIDUtil.uuidFromIntArray(((IntArrayTag) element).getAsIntArray()));

            sorted.sort(Comparator.comparing(UUID::toString));
            var list = new ListTag();
            for (var uuid : sorted)
                list.add(new IntArrayTag(UUIDUtil.uuidToIntArray(uuid)));

            tag.put("registered_players", list);
        }

        if (tag.get("current_mobs") instanceof ListTag mobs) {
            var names = new ArrayList<String>();
            for (var element : mobs) {
                var uuid = UUIDUtil.uuidFromIntArray(((IntArrayTag) element).getAsIntArray());
                names.add(mobName(state, uuid));
            }

            names.sort(Comparator.naturalOrder());
            var list = new ListTag();
            for (var name : names)
                list.add(StringTag.valueOf(name));

            tag.put("current_mobs", list);
        }

        return tag.toString();
    }

    private static String mobName(Trial state, UUID uuid) {
        for (int ordinal = 0; ordinal < state.mobs.size(); ordinal++) {
            if (state.mobs.get(ordinal).getUUID().equals(uuid))
                return "mob " + (ordinal + 1);
        }

        return "unknown";
    }

    // The block entity data the listener got for the spawner this tick, in order.
    private static JsonArray client(List<Packet<?>> heard, Trial state) {
        var sent = new JsonArray();
        for (var packet : heard) {
            if (packet instanceof ClientboundBlockEntityDataPacket data && data.getPos().equals(origin))
                sent.add(data.getTag().toString());
        }

        return sent;
    }

    // The blocks of the area that changed since the last checkpoint, which then become the ones compared against.
    private static JsonArray changes(List<BlockState> blocks) {
        var changed = new JsonArray();
        var index = 0;
        for (var pos : area()) {
            var current = level.getBlockState(pos);
            if (current != blocks.get(index)) {
                changed.add(offset(pos) + "=" + text(current));
                blocks.set(index, current);
            }

            index++;
        }

        return changed;
    }

    // Every entity in the area but players, in the order they were added.
    private static JsonArray entities(Trial state) {
        var found = new ArrayList<Entity>(
            level.getEntities((Entity) null, box(), entity -> !(entity instanceof Player)));
        found.sort(Comparator.comparingInt(Entity::getId));
        var described = new JsonArray();
        for (var entity : found)
            described.add(describe(entity, state));

        return described;
    }

    // Each item's id modulo 4, in the order the entities are listed: a resting item only moves on the ticks its age
    // and id pick, and ids can't match between servers, so the replay takes vanilla's.
    private static JsonArray itemPhases() {
        var found = new ArrayList<Entity>(
            level.getEntities((Entity) null, box(), entity -> entity instanceof ItemEntity));
        found.sort(Comparator.comparingInt(Entity::getId));
        var phases = new JsonArray();
        for (var entity : found)
            phases.add(entity.getId() % 4);

        return phases;
    }

    private static JsonObject ominousState(Trial state) {
        var result = new JsonObject();
        var effects = new JsonArray();
        for (var player : state.players) {
            for (var holder : List.of(MobEffects.BAD_OMEN, MobEffects.TRIAL_OMEN)) {
                var effect = player.getEffect(holder);
                if (effect != null) {
                    effects.add(player.getGameProfile().name() + " "
                        + BuiltInRegistries.MOB_EFFECT.getKey(holder.value()) + " " + effect.getDuration()
                        + " " + effect.getAmplifier() + " " + effect.isVisible() + " " + effect.showIcon());
                }
            }
        }

        var entities = new ArrayList<Entity>(level.getEntities((Entity) null, box(), entity ->
            entity instanceof Mob || entity instanceof OminousItemSpawner || entity instanceof ItemEntity
                || entity instanceof net.minecraft.world.entity.projectile.throwableitemprojectile.ThrowableItemProjectile));
        entities.sort(Comparator.comparingInt(Entity::getId));
        var data = new JsonArray();
        var ops = level.registryAccess().createSerializationContext(net.minecraft.nbt.NbtOps.INSTANCE);
        for (var entity : entities) {
            if (entity instanceof Mob mob) {
                for (var slot : EquipmentSlot.values()) {
                    var item = mob.getItemBySlot(slot);
                    if (!item.isEmpty()) {
                        var stack = ItemStack.CODEC.encodeStart(ops, item).getOrThrow();
                        data.add(mobName(state, mob.getUUID()) + " " + slot.getName() + " "
                            + mob.getDropChances().byEquipment(slot) + " " + stack);
                    }
                }
            } else if (entity instanceof OminousItemSpawner itemSpawner) {
                // Its age is deliberately unsaved. The fixture records only its own two fields.
                var output = net.minecraft.world.level.storage.TagValueOutput.createWithContext(
                    ProblemReporter.DISCARDING, level.registryAccess());
                entity.saveWithoutId(output);
                var tag = output.buildResult();
                var kept = new CompoundTag();
                if (tag.contains("item"))
                    kept.put("item", tag.get("item").copy());
                kept.putLong("spawn_item_after_ticks", tag.getLongOr("spawn_item_after_ticks", 0));
                data.add("item spawner " + kept);
            } else if (entity instanceof ItemEntity item) {
                var output = net.minecraft.world.level.storage.TagValueOutput.createWithContext(
                    ProblemReporter.DISCARDING, level.registryAccess());
                entity.saveWithoutId(output);
                var saved = output.buildResult();
                var kept = new CompoundTag();
                kept.put("Item", saved.get("Item").copy());
                kept.putShort("PickupDelay", saved.getShortOr("PickupDelay", (short) 0));
                data.add("released item " + kept);
            } else if (entity instanceof
                net.minecraft.world.entity.projectile.throwableitemprojectile.ThrowableItemProjectile projectile) {
                var stack = ItemStack.CODEC.encodeStart(ops, projectile.getItem()).getOrThrow();
                data.add("released projectile " + BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()) + " " + stack);
            }
        }

        result.add("effects", effects);
        result.add("data", data);
        return result;
    }

    private static JsonArray releaseMotion() {
        var result = new JsonArray();
        for (int index = 0; index < 4; index++) {
            var seed = 4913L + index;
            var power = switch (index) {
                case 1 -> 1.1F * 1.25F;
                case 2 -> 1F;
                case 3 -> 0.5F;
                default -> 1.1F;
            };
            var spread = switch (index) {
                case 1 -> 3F;
                case 2 -> 6.6666665F;
                case 3 -> 1F;
                default -> 6F;
            };
            var arrow = new net.minecraft.world.entity.projectile.arrow.Arrow(EntityTypes.ARROW, level);
            arrow.getRandom().setSeed(seed);
            var motion = arrow.getMovementToShoot(0, -1, 0, power, spread);
            var sample = new JsonObject();
            sample.addProperty("seed", seed);
            sample.addProperty("power", power);
            sample.addProperty("spread", spread);
            var vector = new JsonArray();
            vector.add(motion.x);
            vector.add(motion.y);
            vector.add(motion.z);
            sample.add("motion", vector);
            sample.addProperty("nextInt", arrow.getRandom().nextInt());
            result.add(sample);
        }

        return result;
    }

    private static JsonArray itemMotion() {
        var found = new ArrayList<Entity>(
            level.getEntities((Entity) null, box(), entity -> entity instanceof ItemEntity));
        found.sort(Comparator.comparingInt(Entity::getId));
        var motion = new JsonArray();
        for (var entity : found) {
            var row = new JsonArray();
            row.add(entity.getDeltaMovement().x);
            row.add(entity.getDeltaMovement().y);
            row.add(entity.getDeltaMovement().z);
            motion.add(row);
        }

        return motion;
    }

    // Vault drops retain their complete saved stack data, including enchantments and other reward components.
    // Only vaults record this extra field, so trial-spawner captures retain their original item observations.
    private static JsonArray itemStacks() {
        var found = new ArrayList<Entity>(
            level.getEntities((Entity) null, box(), entity -> entity instanceof ItemEntity));
        found.sort(Comparator.comparingInt(Entity::getId));
        var stacks = new JsonArray();
        var ops = level.registryAccess().createSerializationContext(net.minecraft.nbt.NbtOps.INSTANCE);
        for (var entity : found) {
            var item = ((ItemEntity) entity).getItem();
            stacks.add(ItemStack.CODEC.encodeStart(ops, item).getOrThrow().toString());
        }

        return stacks;
    }

    // An entity as "<type> <x,y,z>", then a mob's yaw (the spawner draws it), name and what it holds and wears, its
    // health, whether it must persist, and the item it is. An item's yaw comes from its own unseeded random, so it
    // isn't recorded.
    private static String describe(Entity entity, Trial state) {
        var at = entity.position().subtract(Vec3.atLowerCornerOf(origin));
        var text = new StringBuilder(BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString())
            .append(' ').append(number(at.x, 3)).append(',').append(number(at.y, 3)).append(',')
            .append(number(at.z, 3));
        if (entity instanceof Mob mob) {
            text.append(" yaw ").append(number(entity.getYRot(), 2));
            text.append(" (").append(mobName(state, mob.getUUID())).append(')');
            for (var slot : EquipmentSlot.values()) {
                var worn = mob.getItemBySlot(slot);
                if (!worn.isEmpty())
                    text.append(' ').append(slot.getName()).append(' ').append(stack(worn));
            }
        }
        if (entity instanceof LivingEntity living)
            text.append(" health ").append(number(living.getHealth(), 2));
        if (entity instanceof Mob mob && mob.isPersistenceRequired())
            text.append(" persistent");
        if (entity instanceof ItemEntity item)
            text.append(" item ").append(stack(item.getItem()));
        return text.toString();
    }

    // The sounds, level events and particles the listener received, and the game events heard, in order. Mobs'
    // ambient sounds come from their own unseeded randoms, so they aren't recorded.
    private static void recordEvents(JsonObject output, List<Packet<?>> heard, List<String> gameEvents) {
        var sounds = new JsonArray();
        var levelEvents = new JsonArray();
        var particles = new JsonArray();
        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                if (sound.getSound().value().location().getPath().endsWith(".ambient"))
                    continue;

                var at = new Vec3(sound.getX(), sound.getY(), sound.getZ()).subtract(Vec3.atLowerCornerOf(origin));
                var description = sound.getSound().value().location() + " " + number(at.x, 3) + "," + number(at.y, 3)
                    + "," + number(at.z, 3) + " volume " + number(sound.getVolume(), 2);
                if (machine() instanceof VaultBlockEntity || ominousCapture)
                    description += " pitchBits " + Float.floatToIntBits(sound.getPitch()) + " seed " + sound.getSeed();

                if (ominousCapture)
                    description += " category " + sound.getSource().getName();

                sounds.add(description);
            } else if (packet instanceof ClientboundLevelEventPacket event) {
                levelEvents.add(event.getType() + " " + offset(event.getPos()) + " " + event.getData());
            } else if (packet instanceof ClientboundLevelParticlesPacket particle) {
                var at = new Vec3(particle.x(), particle.y(), particle.z())
                    .subtract(Vec3.atLowerCornerOf(origin));
                particles.add(BuiltInRegistries.PARTICLE_TYPE.getKey(particle.particle().getType()) + " "
                    + number(at.x, 3) + "," + number(at.y, 3) + "," + number(at.z, 3) + " count "
                    + particle.count());
            }
        }

        output.add("sounds", sounds);
        output.add("levelEvents", levelEvents);
        output.add("particles", particles);
        output.add("gameEvents", strings(gameEvents));
    }

    // The level's random's 48-bit state, which shows how many draws the tick made.
    private static long randomState() {
        try {
            var field = level.getRandom().getClass().getDeclaredField("seed");
            field.setAccessible(true);
            return ((AtomicLong) field.get(level.getRandom())).get();
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    private static void unregister(Trial state) {
        level.getChunkAt(origin).getListenerRegistry(SectionPos.blockToSectionCoord(origin.getY()))
            .unregister(state.gameEventListener);
        playerMap().removePlayer(state.listener);
        players(server.getPlayerList()).remove(state.listener);
        level.players().remove(state.listener);
        for (var player : state.players) {
            players(server.getPlayerList()).remove(player);
            level.players().remove(player);
        }
    }

    // A placed structure's trial spawners and vaults: their saved data as placed and after a few ticks, by position
    // relative to the structure's chunk.
    private static void captureChamber(JsonObject input) throws Exception {
        var chunk = input.getAsJsonArray("chunk");
        var center = new ChunkPos(chunk.get(0).getAsInt(), chunk.get(1).getAsInt());
        var radius = input.get("radius").getAsInt();
        origin = new BlockPos(center.getMinBlockX(), 0, center.getMinBlockZ());
        server.submit(() -> {
            for (int x = -radius; x <= radius; x++) {
                for (int z = -radius; z <= radius; z++)
                    level.setChunkForced(center.x() + x, center.z() + z, true);
            }
        }).join();
        for (int x = -radius; x <= radius; x++) {
            for (int z = -radius; z <= radius; z++)
                waitUntilTicking(new BlockPos(origin.getX() + x * 16, 0, origin.getZ() + z * 16));
        }

        var structure = input.get("structure").getAsString();
        var command = "place structure " + structure + " " + origin.getX() + " 0 " + origin.getZ();
        server.submit(() -> server.getCommands().performPrefixedCommand(server.createCommandSourceStack(), command))
            .join();

        var fresh = server.submit(() -> collect(center, radius)).join();

        // Allowed to spawn with natural spawning off, each spawner picks its next spawn data from its registry
        // config as it ticks, which then shows how vanilla reads the config's JSON.
        server.submit(() -> overrideSpawnRules(center, radius)).join();
        step(input.get("ticks").getAsInt());
        var ticked = server.submit(() -> collect(center, radius)).join();
        if (fresh.isEmpty())
            throw new IllegalStateException("The structure placed no trial spawners or vaults.");

        var entries = new JsonArray();
        for (var key : fresh.keySet()) {
            var entry = new JsonObject();
            var pos = BlockPos.of(key);
            entry.addProperty("pos", offset(pos));
            entry.addProperty("id", fresh.get(key)[0]);
            entry.addProperty("state", fresh.get(key)[1]);
            entry.addProperty("fresh", fresh.get(key)[2]);
            entry.addProperty("ticked", ticked.get(key)[2]);
            entries.add(entry);
        }

        input.add("blockEntities", entries);
        server.submit(() -> {
            for (int x = -radius; x <= radius; x++) {
                for (int z = -radius; z <= radius; z++)
                    level.setChunkForced(center.x() + x, center.z() + z, false);
            }
        }).join();
    }

    private static void overrideSpawnRules(ChunkPos center, int radius) {
        for (int x = -radius; x <= radius; x++) {
            for (int z = -radius; z <= radius; z++) {
                var chunk = level.getChunk(center.x() + x, center.z() + z);
                for (var blockEntity : chunk.getBlockEntities().values()) {
                    if (blockEntity instanceof TrialSpawnerBlockEntity spawner)
                        spawner.getTrialSpawner().overridePeacefulAndMobSpawnRule();
                }
            }
        }
    }

    // Each trial spawner and vault in the chunks around the center, by position: its id, state and saved data.
    private static Map<Long, String[]> collect(ChunkPos center, int radius) {
        var found = new java.util.TreeMap<Long, String[]>();
        for (int x = -radius; x <= radius; x++) {
            for (int z = -radius; z <= radius; z++) {
                var chunk = level.getChunk(center.x() + x, center.z() + z);
                for (var blockEntity : chunk.getBlockEntities().values()) {
                    var id = BuiltInRegistries.BLOCK_ENTITY_TYPE.getKey(blockEntity.getType()).toString();
                    if (!id.equals("minecraft:trial_spawner") && !id.equals("minecraft:vault"))
                        continue;

                    var saved = blockEntity.saveWithoutMetadata(level.registryAccess()).toString();
                    found.put(blockEntity.getBlockPos().asLong(),
                        new String[]{id, text(blockEntity.getBlockState()), saved});
                }
            }
        }

        return found;
    }

    // The scratch jar's LevelChunk calls this observer after a changed state is stored, before its side effects.
    private static void observeWrites(JsonArray writes) {
        try {
            var parameters = new Class<?>[]{Object.class, Object.class, Object.class, int.class};
            net.minecraft.world.level.chunk.LevelChunk.class.getDeclaredMethod("obsidianObserveBlockUse", parameters);
        } catch (NoSuchMethodException e) {
            throw new IllegalStateException("The probe needs the observed vanilla jar.", e);
        }

        var box = box();
        java.util.function.Consumer<Object[]> observer = values -> {
            if (values[0] != level || Thread.currentThread() != serverThread)
                return;

            var pos = (BlockPos) values[1];
            if (!box.contains(Vec3.atCenterOf(pos)))
                return;

            writes.add(offset(pos) + " " + text((BlockState) values[2]) + " flags " + values[3]);
        };
        System.getProperties().put("obsidian.blockUse.writes", observer);
    }

    // Runs the level's tick the given number of times, on the server thread between the server's own ticks.
    private static void step(int ticks) {
        server.submit(() -> {
            for (int tick = 0; tick < ticks; tick++)
                tickOnce();
        }).join();
    }

    // One tick of the level, on the server thread: the frozen game steps one tick for its length, so that the
    // server's loop never ticks the level in between.
    private static void tickOnce() {
        var manager = server.tickRateManager();
        manager.setFrozenTicksToRun(1);
        manager.tick();
        level.tick(() -> true);
        manager.tick();
    }

    // Sends the block and block entity changes waiting for the end of the tick, as the chunk source does then.
    private static void broadcastChanges() {
        try {
            var method = ServerChunkCache.class.getDeclaredMethod("broadcastChangedChunks", ProfilerFiller.class);
            method.setAccessible(true);
            method.invoke(level.getChunkSource(), Profiler.get());
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    private static PlayerMap playerMap() {
        try {
            var field = net.minecraft.server.level.ChunkMap.class.getDeclaredField("playerMap");
            field.setAccessible(true);
            return (PlayerMap) field.get(level.getChunkSource().chunkMap);
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    private static void clear() {
        var air = Blocks.AIR.defaultBlockState();
        for (var pos : area())
            level.setBlock(pos, air, QuietFlags);
    }

    private static void discardEntities() {
        for (var entity : level.getEntities((Entity) null, chunkBox(), entity -> !(entity instanceof Player)))
            entity.discard();
    }

    private static List<BlockState> snapshot() {
        var states = new ArrayList<BlockState>();
        for (var pos : area())
            states.add(level.getBlockState(pos));

        return states;
    }

    // The compared area in a fixed order: y, then z, then x ascending.
    private static List<BlockPos> area() {
        var positions = new ArrayList<BlockPos>();
        for (int y = -Below; y <= Above; y++) {
            for (int z = -Reach; z <= Reach; z++) {
                for (int x = -Reach; x <= Reach; x++)
                    positions.add(origin.offset(x, y, z));
            }
        }

        return positions;
    }

    private static AABB box() {
        var from = Vec3.atLowerCornerOf(origin.offset(-Reach, -Below, -Reach));
        var to = Vec3.atLowerCornerOf(origin.offset(Reach + 1, Above + 1, Reach + 1));
        return new AABB(from, to);
    }

    // The origin's whole chunk, from the bottom of the world to the build height.
    private static AABB chunkBox() {
        var minX = (origin.getX() >> 4) << 4;
        var minZ = (origin.getZ() >> 4) << 4;
        return new AABB(minX, level.getMinY(), minZ, minX + 16, level.getMaxY() + 16, minZ + 16);
    }

    private static String offset(BlockPos pos) {
        var relative = pos.subtract(origin);
        return relative.getX() + "," + relative.getY() + "," + relative.getZ();
    }

    private static String number(double value, int decimals) {
        return String.format(Locale.ROOT, "%." + decimals + "f", value);
    }

    // An item stack as "<id> x<count>".
    private static String stack(ItemStack stack) {
        return BuiltInRegistries.ITEM.getKey(stack.getItem()) + " x" + stack.getCount();
    }

    private static CompoundTag parse(String snbt) {
        try {
            return TagParser.parseCompoundFully(snbt);
        } catch (Exception e) {
            throw new IllegalArgumentException("Bad SNBT: " + snbt, e);
        }
    }

    private static GameProfile profile(String name) {
        return new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
    }

    @SuppressWarnings("unchecked")
    private static List<ServerPlayer> players(PlayerList list) {
        try {
            var field = PlayerList.class.getDeclaredField("players");
            field.setAccessible(true);
            return (List<ServerPlayer>) field.get(list);
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    // A game connection that keeps the packets sent to it instead of sending them. Its player is set, as untracking an
    // entity the player saw reads it.
    private static ServerGamePacketListenerImpl recorder(ServerPlayer player, List<Packet<?>> packets) {
        try {
            var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
            unsafeField.setAccessible(true);
            var unsafe = unsafeField.get(null);
            var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
            var recorder = (Recorder) allocate.invoke(unsafe, Recorder.class);
            recorder.packets = packets;
            recorder.player = player;

            // The server sends each player's next chunks every tick; this one is sent none.
            var chunkSender = ServerGamePacketListenerImpl.class.getDeclaredField("chunkSender");
            chunkSender.setAccessible(true);
            chunkSender.set(recorder, new net.minecraft.server.network.PlayerChunkSender(true));
            return recorder;
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
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

    private static Vec3 vector(JsonArray values) {
        return new Vec3(values.get(0).getAsDouble(), values.get(1).getAsDouble(), values.get(2).getAsDouble());
    }

    private static JsonArray strings(List<String> values) {
        var array = new JsonArray();
        for (var value : values)
            array.add(value);

        return array;
    }

    // Keeps each short array of numbers on one line.
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

    // Load fixture tables through vanilla's data-pack loader, so location predicates run on the real server level.
    private static void installLootTables(JsonObject root) throws Exception {
        if (!root.has("lootTables"))
            return;

        net.minecraft.SharedConstants.tryDetectVersion();
        var format = net.minecraft.SharedConstants.getCurrentVersion()
            .packVersion(net.minecraft.server.packs.PackType.SERVER_DATA);
        var metadata = new JsonObject();
        var pack = new JsonObject();
        pack.addProperty("description", "Vault fixture loot");
        var version = new JsonArray();
        version.add(format.major());
        version.add(format.minor());
        pack.add("min_format", version);
        pack.add("max_format", version.deepCopy());
        metadata.add("pack", pack);

        var folder = Path.of("world", "datapacks", "vault-fixtures");
        Files.createDirectories(folder);
        Files.writeString(folder.resolve("pack.mcmeta"), metadata.toString());
        for (var entry : root.getAsJsonObject("lootTables").entrySet()) {
            var id = Identifier.parse(entry.getKey());
            var path = folder.resolve("data").resolve(id.getNamespace()).resolve("loot_table")
                .resolve(id.getPath() + ".json");
            Files.createDirectories(path.getParent());
            Files.writeString(path, entry.getValue().toString());
        }
    }

    private static void startServer() throws Exception {
        Files.writeString(Path.of("eula.txt"), "eula=true\n");

        // A flat world of air in plains, with no watchdog, since a case's setup runs as one task on the server thread.
        // Its seed is the replay world's, as the loot tables' random sequences come from it.
        var properties = String.join("\n",
            "level-type=minecraft\\:flat",
            "level-seed=test-seed",
            "generator-settings={\"layers\"\\:[{\"block\"\\:\"minecraft\\:air\",\"height\"\\:1}],"
                + "\"biome\"\\:\"minecraft\\:plains\"}",
            "generate-structures=false",
            "spawn-protection=0",
            "gamemode=survival",
            "difficulty=normal",
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

    /** A player the probe places: a spawner's visitor, or the spectator that hears the case. */
    private static final class ProbePlayer extends ServerPlayer {
        ProbePlayer(MinecraftServer server, ServerLevel level, GameProfile profile) {
            super(server, level, profile, ClientInformation.createDefault());
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

        // There is no channel to hold back or flush.
        @Override public void suspendFlushing() {
        }

        @Override public void resumeFlushing() {
        }
    }

    /** Records every game event within 16 blocks of the origin, by name, offset and source. */
    private static final class Listener implements GameEventListener {
        private final BlockPos target;
        private final List<String> events;

        Listener(BlockPos target, List<String> events) {
            this.target = target;
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
            var source = context.sourceEntity() == null
                ? "none" : BuiltInRegistries.ENTITY_TYPE.getKey(context.sourceEntity().getType()).toString();
            events.add(name + " " + block.getX() + "," + block.getY() + "," + block.getZ() + " " + source);
            return true;
        }
    }

    // Makes vanilla's SpecialDates.isHalloween answer as it would on date: it compares today's local date with its
    // HALLOWEEN constant, so the probe moves the constant by as many days as date is from today. The comparison, and
    // the code reading it, stay vanilla's. (As EyeblossomGhastPortalFixtures does.)
    private static void runOn(LocalDate date) throws Exception {
        SpecialDates.isHalloween();
        var today = LocalDate.now();
        var halloween = LocalDate.of(date.getYear(), Month.OCTOBER, 31);
        var shifted = today.plusDays(ChronoUnit.DAYS.between(date, halloween));
        var field = SpecialDates.class.getField("HALLOWEEN");
        var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var type = unsafe.getClass();
        var base = type.getMethod("staticFieldBase", Field.class).invoke(unsafe, field);
        var offset = (long)type.getMethod("staticFieldOffset", Field.class).invoke(unsafe, field);
        type.getMethod("putObject", Object.class, long.class, Object.class)
            .invoke(unsafe, base, offset, MonthDay.from(shifted));
        if (SpecialDates.isHalloween() != MonthDay.from(date).equals(MonthDay.of(Month.OCTOBER, 31))) {
            throw new IllegalStateException("Could not move vanilla's Halloween to " + date);
        }
    }
}
