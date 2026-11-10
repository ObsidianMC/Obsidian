import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonElement;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import io.netty.channel.ChannelFutureListener;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Comparator;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.players.PlayerList;
import net.minecraft.world.Difficulty;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.LightningBolt;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.animal.cow.MushroomCow;
import net.minecraft.world.entity.animal.equine.SkeletonHorse;
import net.minecraft.world.entity.animal.golem.CopperGolem;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.monster.Creeper;
import net.minecraft.world.entity.monster.Witch;
import net.minecraft.world.entity.monster.zombie.ZombifiedPiglin;
import net.minecraft.world.item.Items;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.WeatheringCopper;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.gameevent.BlockPositionSource;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gameevent.PositionSource;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Records lightning bolts on a headless dedicated server in a flat world of air, one game tick at a time.
 *
 * <p>Each case is its input: the blocks and mobs set up around its origin, the difficulty, the fire spread radius, the
 * seeds of the level's random and of the bolt's own random, and where the bolt is summoned. A {@code strike} case adds
 * a bolt as {@code /summon} does (or with a cause, as a Channeling trident does, or visual only, as a skeleton trap's
 * bolt is) and steps the frozen game one tick at a time, recording after each tick the blocks the level stored (in
 * order, with their flags), the blocks that changed, the entities in the area (type, position, health, remaining fire
 * ticks and what lightning changes), and the sounds and level events a spectator beside the strike received and the
 * game events within 16 blocks. At the end it records the lightning advancements the spectator was granted. A
 * {@code natural} case runs {@code ServerLevel.tickThunder} once on the origin's chunk in a thunderstorm, with the level
 * random seeded and {@code randValue} set, and records the entities it added, {@code randValue} afterwards and the
 * level random's next {@code nextInt()}, which shows the draws it made.
 *
 * <p>The spectator stands where the bolt can't hit it. Spectators don't count for natural spawning, so no chunk is
 * shuffled with the level random, which the strike's fire and copper draws share.
 */
public final class LightningFixtures {
    // Set without neighbor or shape updates and without drops, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // How far from the origin the area that is cleared, watched and compared reaches.
    private static final int Reach = 6;
    private static final int Below = 2;
    private static final int Above = 8;

    // The cases run Spacing blocks apart along x, each in a chunk of its own.
    private static final int Spacing = 32;

    // Ticks run after a case's area is cleared, so that ticks it left scheduled find air.
    private static final int DrainTicks = 3;

    // The listener stands this far from the origin: outside the bolt's reach, within its witnesses' box.
    private static final Vec3 ListenerOffset = new Vec3(5.5, 10, 0.5);

    private static final List<String> Advancements = List.of(
        "minecraft:adventure/lightning_rod_with_villager_no_fire",
        "minecraft:adventure/very_very_frightening");

    private static MinecraftServer server;
    private static ServerLevel level;
    private static Thread serverThread;
    private static BlockPos origin;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();
        var selected = args.length > 1 && !args[1].isBlank() ? Set.of(args[1].split("\\+")) : Set.<String>of();

        try {
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

                capture(input, index);
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
        var gson = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(fixturePath, compact(gson.toJson(root)) + "\n", StandardCharsets.UTF_8);
        System.out.println("Wrote " + fixturePath);
        System.out.flush();

        // The world is thrown away, so skip the server's save on exit.
        Runtime.getRuntime().halt(0);
    }

    // Nothing moves unless stepped: the game is frozen, and time, weather and random ticks are off. Each case's chunk
    // is forced, so its blocks and entities tick when the game steps.
    private static void configure(JsonArray cases) {
        server.tickRateManager().setFrozen(true);
        var rules = server.getGameRules();
        rules.set(GameRules.ADVANCE_TIME, false, server);
        rules.set(GameRules.ADVANCE_WEATHER, false, server);
        rules.set(GameRules.SPAWN_MOBS, false, server);
        rules.set(GameRules.RANDOM_TICK_SPEED, 0, server);
        for (int index = 0; index < cases.size(); index++) {
            var at = originOf(index);
            level.setChunkForced(at.getX() >> 4, at.getZ() >> 4, true);
        }
    }

    private static void waitUntilTicking(JsonArray cases) throws Exception {
        for (int index = 0; index < cases.size(); index++) {
            var at = originOf(index);
            var chunk = net.minecraft.world.level.ChunkPos.containing(at).pack();
            while (!server.submit(() -> level.shouldTickBlocksAt(chunk)
                && level.areEntitiesLoaded(chunk)
                && level.isPositionEntityTicking(at)).join()) {
                Thread.sleep(50);
            }
        }
    }

    /** Where the case at <paramref name="index"/> runs: the middle of a chunk of its own, at y 64. */
    private static BlockPos originOf(int index) {
        return new BlockPos(index * Spacing + 8, 64, 8);
    }

    private static void capture(JsonObject input, int index) {
        origin = originOf(index);
        server.submit(() -> {
            clear();
            discardEntities();
        }).join();
        step(DrainTicks);

        // The blocks first, then the light they cast, which decides where the sky is seen. Vanilla registers a
        // lightning rod's point of interest in a task of its own, which runs before the next.
        server.submit(() -> {
            applyRules(input);
            setup(input);
        }).join();
        waitForLight();

        var kind = input.has("kind") ? input.get("kind").getAsString() : "strike";
        if (kind.equals("natural")) {
            server.submit(() -> {
                spawnEntities(input);
                captureNatural(input);
            }).join();
            return;
        }

        captureStrike(input, index);
    }

    private static void waitForLight() {
        var engine = level.getChunkSource().getLightEngine();
        for (var attempt = 0; attempt < 2400; attempt++) {
            var pending = server.submit(() -> {
                engine.tryScheduleUpdate();
                return engine.waitForPendingTasks(0, 0);
            }).join();
            pending.join();

            boolean done = server.submit(() -> !engine.hasLightWork()).join();
            if (done && attempt > 1)
                return;

            try {
                Thread.sleep(50);
            } catch (InterruptedException e) {
                throw new RuntimeException(e);
            }
        }

        throw new IllegalStateException("The light engine didn't settle.");
    }

    private static void captureStrike(JsonObject input, int index) {
        var heard = new ArrayList<Packet<?>>();
        var gameEvents = new ArrayList<String>();
        var writes = new JsonArray();
        var state = server.submit(() -> prepareStrike(input, index, heard, gameEvents, writes)).join();

        var checkpoints = new JsonArray();
        var ticks = input.get("ticks").getAsInt();
        for (int tick = 1; tick <= ticks; tick++) {
            step(1);
            var checkpoint = server.submit(() -> checkpoint(state, heard, gameEvents, writes)).join();
            checkpoints.add(checkpoint);
        }

        input.add("checkpoints", checkpoints);
        input.add("advancements", server.submit(() -> granted(state.listener)).join());
        server.submit(() -> {
            System.getProperties().remove("obsidian.blockUse.writes");
            unregister(state);
            clear();
            discardEntities();
        }).join();
    }

    /** What a strike case keeps between its ticks. */
    private record Strike(ProbePlayer listener, Listener gameEventListener, List<BlockState> blocks) {
    }

    private static Strike prepareStrike(JsonObject input, int index, List<Packet<?>> heard, List<String> gameEvents,
        JsonArray writes) {
        spawnEntities(input);

        // A spectator of its own for each case, so it starts without advancements, hears the area and is never hit.
        var listener = new ProbePlayer(server, level, profile("Listener" + index));
        listener.connection = recorder(heard);
        listener.setGameMode(GameType.SPECTATOR);
        var at = Vec3.atLowerCornerOf(origin).add(ListenerOffset);
        listener.snapTo(at.x, at.y, at.z, 0, 0);
        players(server.getPlayerList()).add(listener);
        level.players().add(listener);

        var gameEventListener = new Listener(origin, gameEvents);
        level.getChunkAt(origin).getListenerRegistry(SectionPos.blockToSectionCoord(origin.getY()))
            .register(gameEventListener);

        level.getRandom().setSeed(input.get("seed").getAsLong());
        observeWrites(writes);

        var bolt = (LightningBolt) BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse("minecraft:lightning_bolt"))
            .create(level, EntitySpawnReason.COMMAND);
        reseed(bolt, input.get("boltSeed").getAsLong());
        var position = Vec3.atLowerCornerOf(origin).add(vector(input.getAsJsonArray("bolt")));
        bolt.snapTo(position.x, position.y, position.z, 0, 0);
        if (input.has("visualOnly") && input.get("visualOnly").getAsBoolean())
            bolt.setVisualOnly(true);
        if (input.has("cause") && input.get("cause").getAsBoolean())
            bolt.setCause(listener);
        level.addFreshEntity(bolt);

        return new Strike(listener, gameEventListener, snapshot());
    }

    // The case's difficulty (normal by default), fire spread radius around players (-1, everywhere, by default), mob
    // spawning (off by default) and mob drops (on by default).
    private static void applyRules(JsonObject input) {
        var difficulty = input.has("difficulty") ? input.get("difficulty").getAsString() : "normal";
        server.setDifficulty(Difficulty.byName(difficulty), true);
        var radius = input.has("fireSpread") ? input.get("fireSpread").getAsInt() : -1;
        server.getGameRules().set(GameRules.FIRE_SPREAD_RADIUS_AROUND_PLAYER, radius, server);
        var spawnMobs = input.has("spawnMobs") && input.get("spawnMobs").getAsBoolean();
        server.getGameRules().set(GameRules.SPAWN_MOBS, spawnMobs, server);
        var mobDrops = !input.has("mobDrops") || input.get("mobDrops").getAsBoolean();
        server.getGameRules().set(GameRules.MOB_DROPS, mobDrops, server);
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

    // The case's mobs and items, without AI so that they stay where they're put, in the fixture's order.
    private static void spawnEntities(JsonObject input) {
        if (!input.has("entities"))
            return;

        for (var element : input.getAsJsonArray("entities")) {
            var spawn = element.getAsJsonObject();
            var type = spawn.get("type").getAsString();
            var at = Vec3.atLowerCornerOf(origin).add(vector(spawn.getAsJsonArray("pos")));
            Entity entity;
            if (type.equals("minecraft:item")) {
                var item = BuiltInRegistries.ITEM.getValue(Identifier.parse(spawn.get("item").getAsString()));
                var stack = new ItemStack(item);
                entity = new ItemEntity(level, at.x, at.y, at.z, stack, 0, 0, 0);
            } else {
                entity = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse(type))
                    .create(level, EntitySpawnReason.COMMAND);
                entity.snapTo(at.x, at.y, at.z, 0, 0);
            }

            if (entity instanceof Mob mob)
                mob.setNoAi(true);
            if (spawn.has("variant") && entity instanceof MushroomCow cow)
                setMooshroomVariant(cow, spawn.get("variant").getAsString());
            if (spawn.has("weatherState") && entity instanceof CopperGolem golem)
                golem.setWeatherState(WeatheringCopper.WeatherState.valueOf(
                    spawn.get("weatherState").getAsString().toUpperCase(Locale.ROOT)));
            level.addFreshEntity(entity);
        }
    }

    private static void setMooshroomVariant(MushroomCow cow, String name) {
        try {
            var variant = MushroomCow.Variant.valueOf(name.toUpperCase(Locale.ROOT));
            var method = MushroomCow.class.getDeclaredMethod("setVariant", MushroomCow.Variant.class);
            method.setAccessible(true);
            method.invoke(cow, variant);
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    // The bolt's own random seeded with the case's seed, then its seed and flashes drawn again in the constructor's
    // order, as a bolt made with that random draws them.
    private static void reseed(LightningBolt bolt, long seed) {
        try {
            var randomField = Entity.class.getDeclaredField("random");
            randomField.setAccessible(true);
            var random = (net.minecraft.util.RandomSource) randomField.get(bolt);
            random.setSeed(seed);
            bolt.seed = random.nextLong();
            var flashes = LightningBolt.class.getDeclaredField("flashes");
            flashes.setAccessible(true);
            flashes.setInt(bolt, random.nextInt(3) + 1);
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    private static JsonObject checkpoint(Strike state, List<Packet<?>> heard, List<String> gameEvents,
        JsonArray writes) {
        var checkpoint = new JsonObject();
        checkpoint.add("writes", writes.deepCopy());
        while (!writes.isEmpty())
            writes.remove(0);

        checkpoint.add("changes", changes(state.blocks));
        checkpoint.add("entities", entities());
        recordEvents(checkpoint, heard, gameEvents);
        heard.clear();
        gameEvents.clear();
        return checkpoint;
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
    private static JsonArray entities() {
        var found = new ArrayList<Entity>(level.getEntities((Entity) null, box(), entity -> !(entity instanceof Player)));
        found.sort(Comparator.comparingInt(Entity::getId));
        var described = new JsonArray();
        for (var entity : found)
            described.add(describe(entity));

        return described;
    }

    // An entity as "<type> <x,y,z> fire <ticks>", then its health and what lightning changes about it.
    private static String describe(Entity entity) {
        var at = entity.position().subtract(Vec3.atLowerCornerOf(origin));
        var text = new StringBuilder(BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString())
            .append(' ').append(number(at.x, 3)).append(',').append(number(at.y, 3)).append(',')
            .append(number(at.z, 3))
            .append(" fire ").append(entity.getRemainingFireTicks());
        if (entity instanceof LivingEntity living)
            text.append(" health ").append(number(living.getHealth(), 2));
        if (entity instanceof Mob mob && mob.isPersistenceRequired())
            text.append(" persistent");
        if (entity instanceof Creeper creeper && creeper.isPowered())
            text.append(" powered");
        if (entity instanceof MushroomCow cow)
            text.append(" variant ").append(cow.getVariant().getSerializedName());
        if (entity instanceof CopperGolem golem)
            text.append(" weather ").append(golem.getWeatherState().name().toLowerCase(Locale.ROOT));
        if (entity instanceof ItemEntity item)
            text.append(" item ").append(stack(item.getItem()));
        if (entity instanceof Mob mob && !(entity instanceof Witch) && !mob.getMainHandItem().isEmpty())
            text.append(" holding ").append(held(mob));
        if (entity instanceof SkeletonHorse horse && horse.isTrap())
            text.append(" trap");
        return text.toString();
    }

    // What a mob holds. A zombified piglin a pig became holds a golden spear or sword by its own random, which isn't
    // seeded, so which isn't recorded.
    private static String held(Mob mob) {
        var held = mob.getMainHandItem();
        if (mob instanceof ZombifiedPiglin && (held.is(Items.GOLDEN_SWORD) || held.is(Items.GOLDEN_SPEAR)))
            return "golden sword or spear";

        return stack(held);
    }

    // The sounds and level events the listener received, and the game events heard, in order.
    private static void recordEvents(JsonObject output, List<Packet<?>> heard, List<String> gameEvents) {
        var sounds = new JsonArray();
        var levelEvents = new JsonArray();
        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                if (byMobRandom(sound.getSound().value().location().getPath()))
                    continue;

                var at = new Vec3(sound.getX(), sound.getY(), sound.getZ()).subtract(Vec3.atLowerCornerOf(origin));
                sounds.add(sound.getSound().value().location() + " " + number(at.x, 3) + "," + number(at.y, 3) + ","
                    + number(at.z, 3) + " volume " + number(sound.getVolume(), 2));
            } else if (packet instanceof ClientboundLevelEventPacket event) {
                levelEvents.add(event.getType() + " " + offset(event.getPos()) + " " + event.getData());
            }
        }

        output.add("sounds", sounds);
        output.add("levelEvents", levelEvents);
        var events = new ArrayList<String>();
        for (var event : gameEvents) {
            if (!event.endsWith(" minecraft:witch"))
                events.add(event);
        }

        output.add("gameEvents", strings(events));
    }

    // Mobs' own randoms aren't seeded, so what they decide with them isn't recorded: their ambient sounds, and a
    // witch drinking (its potion, its sounds and its game events).
    private static boolean byMobRandom(String sound) {
        return sound.endsWith(".ambient") || sound.startsWith("entity.witch.");
    }

    private static JsonArray granted(ProbePlayer listener) {
        var granted = new JsonArray();
        for (var name : Advancements) {
            var holder = server.getAdvancements().get(Identifier.parse(name));
            if (listener.getAdvancements().getOrStartProgress(holder).isDone())
                granted.add(name);
        }

        return granted;
    }

    private static void unregister(Strike state) {
        level.getChunkAt(origin).getListenerRegistry(SectionPos.blockToSectionCoord(origin.getY()))
            .unregister(state.gameEventListener);
        players(server.getPlayerList()).remove(state.listener);
        level.players().remove(state.listener);
    }

    // The natural strike: ServerLevel.tickThunder on the origin's chunk, raining and thundering, with the level random
    // seeded and randValue set.
    private static void captureNatural(JsonObject input) {
        var chunk = level.getChunkAt(origin);
        chunk.setInhabitedTime(0);
        var existing = level.getEntities((Entity) null, chunkBox(), entity -> true).stream().map(Entity::getId).toList();

        var raining = !input.has("raining") || input.get("raining").getAsBoolean();
        var thundering = !input.has("thundering") || input.get("thundering").getAsBoolean();
        level.setRainLevel(raining ? 1 : 0);
        level.setThunderLevel(thundering ? 1 : 0);
        level.getRandom().setSeed(input.get("seed").getAsLong());
        setRandValue(input.get("randValue").getAsInt());
        input.addProperty("effectiveDifficulty", level.getCurrentDifficultyAt(origin).getEffectiveDifficulty());

        level.tickThunder(chunk);

        var added = new JsonArray();
        for (var entity : level.getEntities((Entity) null, chunkBox(), entity -> !existing.contains(entity.getId()))) {
            var text = describe(entity);
            if (entity instanceof LightningBolt bolt && visualOnly(bolt))
                text += " visual";
            added.add(text);
        }

        input.add("added", added);
        input.addProperty("randValueAfter", randValue());
        input.addProperty("next", level.getRandom().nextInt());

        level.setRainLevel(0);
        level.setThunderLevel(0);
        clear();
        discardEntities();
    }

    private static boolean visualOnly(LightningBolt bolt) {
        try {
            var field = LightningBolt.class.getDeclaredField("visualOnly");
            field.setAccessible(true);
            return field.getBoolean(bolt);
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    private static void setRandValue(int value) {
        try {
            var field = Level.class.getDeclaredField("randValue");
            field.setAccessible(true);
            field.setInt(level, value);
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
    }

    private static int randValue() {
        try {
            var field = Level.class.getDeclaredField("randValue");
            field.setAccessible(true);
            return field.getInt(level);
        } catch (ReflectiveOperationException e) {
            throw new RuntimeException(e);
        }
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

    // Runs the level's tick the given number of times, on the server thread between the server's own ticks: the
    // frozen game steps one tick for the length of each, so that the server's loop never ticks the level in between.
    private static void step(int ticks) {
        server.submit(() -> {
            var manager = server.tickRateManager();
            for (int tick = 0; tick < ticks; tick++) {
                manager.setFrozenTicksToRun(1);
                manager.tick();
                level.tick(() -> true);
                manager.tick();
            }
        }).join();
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

    // The origin's whole chunk, from the bottom of the area to the build height.
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

    // A game connection that keeps the packets sent to it instead of sending them.
    private static ServerGamePacketListenerImpl recorder(List<Packet<?>> packets) {
        try {
            var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
            unsafeField.setAccessible(true);
            var unsafe = unsafeField.get(null);
            var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
            var recorder = (Recorder) allocate.invoke(unsafe, Recorder.class);
            recorder.packets = packets;

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

    /** A spectator that hears the case. */
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
}
