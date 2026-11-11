import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.authlib.GameProfile;
import io.netty.channel.ChannelFutureListener;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.UUID;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.SectionPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.protocol.Packet;
import net.minecraft.network.protocol.game.ClientboundLevelEventPacket;
import net.minecraft.network.protocol.game.ClientboundLevelParticlesPacket;
import net.minecraft.network.protocol.game.ClientboundSoundEntityPacket;
import net.minecraft.network.protocol.game.ClientboundSoundPacket;
import net.minecraft.resources.Identifier;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.level.ClientInformation;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.server.network.ServerGamePacketListenerImpl;
import net.minecraft.server.players.PlayerList;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntitySpawnReason;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.Mob;
import net.minecraft.world.entity.Shearable;
import net.minecraft.world.entity.animal.equine.AbstractHorse;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.entity.item.PrimedTnt;
import net.minecraft.world.entity.projectile.Projectile;
import net.minecraft.world.entity.projectile.ItemSupplier;
import net.minecraft.world.entity.projectile.FireworkRocketEntity;
import net.minecraft.world.entity.projectile.arrow.AbstractArrow;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.DispenserBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.gameevent.BlockPositionSource;
import net.minecraft.world.level.gameevent.GameEvent;
import net.minecraft.world.level.gameevent.GameEventListener;
import net.minecraft.world.level.gameevent.PositionSource;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Records what a dispenser does with each item, tick by tick, on a headless dedicated server in a flat world of air
 * whose game is frozen and stepped one tick at a time.
 *
 * <p>Each case of the fixture is its input: the dispenser's facing and items, the blocks set up around it and the
 * mobs standing there (without AI). The probe sets them up without updates, then powers the dispenser by placing a
 * redstone block behind it with every update, and steps the game the case's number of ticks. After each tick it
 * records the blocks of the area that changed since before the redstone block, the dispenser's items, the entities
 * in the area, and the sounds, level events and particles a player beside the dispenser received and the game events
 * around it during that tick.
 *
 * <p>Mobs' ambient sounds aren't recorded: Obsidian's mobs don't make them yet, and when vanilla's play is random.
 * Random ticks are off, and so is mob griefing, so that a snow golem doesn't lay snow where it stands.
 */
public final class DispenserFixtures {
    // How far from the dispenser the cleared and compared area reaches.
    private static final int Reach = 3;

    // How far from the dispenser entities are recorded.
    private static final int EntityReach = 5;

    // Set without neighbor or shape updates and without drops, like the replay's setup.
    private static final int QuietFlags =
        Block.UPDATE_CLIENTS | Block.UPDATE_KNOWN_SHAPE | Block.UPDATE_SUPPRESS_DROPS | Block.UPDATE_SKIP_ON_PLACE;

    // Equipment slots in the order entities' descriptions list them.
    private static final EquipmentSlot[] Slots = {
        EquipmentSlot.HEAD, EquipmentSlot.CHEST, EquipmentSlot.LEGS, EquipmentSlot.FEET,
        EquipmentSlot.MAINHAND, EquipmentSlot.OFFHAND, EquipmentSlot.BODY, EquipmentSlot.SADDLE
    };

    private static MinecraftServer server;

    // Positions in recorded events are written relative to the dispenser.
    private static BlockPos origin;

    public static void main(String[] args) throws Exception {
        var fixturePath = Path.of(args[0]);
        var root = JsonParser.parseString(Files.readString(fixturePath)).getAsJsonObject();
        var selected = args.length > 1 && !args[1].isBlank() ? Set.of(args[1].split("\\+")) : Set.<String>of();

        try {
            startServer();
            origin = position(root.getAsJsonArray("dispenser"));
            forceArea();
            capture(root, selected);
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

    // Forces the chunks around the dispenser and waits until they tick blocks and entities, then freezes the game.
    private static void forceArea() throws Exception {
        var level = server.overworld();
        server.submit(() -> {
            for (int x = (origin.getX() - EntityReach - 1) >> 4; x <= (origin.getX() + EntityReach + 1) >> 4; x++) {
                for (int z = (origin.getZ() - EntityReach - 1) >> 4; z <= (origin.getZ() + EntityReach + 1) >> 4; z++)
                    level.setChunkForced(x, z, true);
            }
        }).join();

        var chunk = net.minecraft.world.level.ChunkPos.containing(origin).pack();
        while (!server.submit(() -> ready(level, chunk)).join())
            Thread.sleep(50);

        server.submit(() -> server.tickRateManager().setFrozen(true)).join();
    }

    private static boolean ready(ServerLevel level, long chunk) {
        return level.shouldTickBlocksAt(chunk)
            && level.areEntitiesLoaded(chunk)
            && level.isPositionEntityTicking(origin);
    }

    private static void capture(JsonObject root, Set<String> selected) throws Exception {
        var level = server.overworld();

        // A player beside the dispenser hears what is broadcast around it, out of every case's line of fire.
        var bystander = new ServerPlayer(server, level, profile("Bystander"), ClientInformation.createDefault());
        var heard = new ArrayList<Packet<?>>();
        bystander.connection = recorder(heard);
        server.submit(() -> {
            bystander.snapTo(origin.getX() + 0.5, origin.getY(), origin.getZ() - 3.5, 0, 0);
            try {
                players(server.getPlayerList()).add(bystander);
            } catch (Exception e) {
                throw new RuntimeException(e);
            }

            level.players().add(bystander);
        }).join();

        var gameEvents = new ArrayList<String>();
        var listener = new Listener(gameEvents);
        server.submit(() -> level.getChunkAt(origin)
            .getListenerRegistry(SectionPos.blockToSectionCoord(origin.getY()))
            .register(listener)).join();

        var count = 0;
        for (var element : root.getAsJsonArray("cases")) {
            var input = element.getAsJsonObject();
            if (!selected.isEmpty() && !selected.contains(input.get("name").getAsString()))
                continue;

            var before = server.submit(() -> setUp(level, input)).join();
            heard.clear();
            gameEvents.clear();

            // Power the dispenser from behind it: it fires four ticks later.
            var facing = facing(input);
            server.submit(() -> level.setBlock(origin.relative(facing.getOpposite()),
                Blocks.REDSTONE_BLOCK.defaultBlockState(), Block.UPDATE_ALL)).join();

            var checkpoints = new JsonArray();
            var ticks = input.has("ticks") ? input.get("ticks").getAsInt() : root.get("ticks").getAsInt();
            for (int tick = 1; tick <= ticks; tick++) {
                step(level);
                var number = tick;
                var checkpoint = server.submit(() -> checkpoint(level, input, number, before, heard, gameEvents))
                    .join();
                heard.clear();
                gameEvents.clear();
                checkpoints.add(checkpoint);
            }

            input.add("checkpoints", checkpoints);
            count++;
        }

        server.submit(() -> {
            clear(level);
            discardEntities(level);
        }).join();
        System.out.println("Fired " + count + " dispensers");
    }

    // The case's blocks, dispenser and mobs in a cleared area; returns the area's states before the dispenser is
    // powered.
    private static List<BlockState> setUp(ServerLevel level, JsonObject input) {
        clear(level);
        discardEntities(level);

        if (input.has("setup")) {
            for (var setup : input.getAsJsonArray("setup")) {
                var placement = setup.getAsJsonObject();
                var pos = origin.offset(position(placement.getAsJsonArray("offset")));
                level.setBlock(pos, state(placement.get("state").getAsString()), QuietFlags);
            }
        }

        var dispenser = Blocks.DISPENSER.defaultBlockState()
            .setValue(net.minecraft.world.level.block.DispenserBlock.FACING, facing(input));
        level.setBlock(origin, dispenser, QuietFlags);
        var blockEntity = (DispenserBlockEntity) level.getBlockEntity(origin);
        if (input.has("items")) {
            for (var value : input.getAsJsonArray("items")) {
                var item = value.getAsJsonObject();
                var stack = new ItemStack(
                    BuiltInRegistries.ITEM.getValue(Identifier.parse(item.get("id").getAsString())),
                    item.get("count").getAsInt());
                if (item.has("nbt")) {
                    try {
                        var tag = net.minecraft.nbt.TagParser.parseCompoundFully(item.get("nbt").getAsString());
                        var ops = level.registryAccess().createSerializationContext(net.minecraft.nbt.NbtOps.INSTANCE);
                        stack = ItemStack.CODEC.parse(ops, tag).getOrThrow();
                    } catch (Exception exception) {
                        throw new IllegalArgumentException("Invalid dispenser stack", exception);
                    }
                }
                blockEntity.setItem(item.get("slot").getAsInt(), stack);
            }
        }

        if (input.has("entities")) {
            for (var value : input.getAsJsonArray("entities"))
                spawn(level, value.getAsJsonObject());
        }

        return snapshot(level);
    }

    // A mob without AI, standing at the bottom centre of its block, facing south, as the case describes it.
    private static void spawn(ServerLevel level, JsonObject input) {
        var type = BuiltInRegistries.ENTITY_TYPE.getValue(Identifier.parse(input.get("type").getAsString()));
        var entity = type.create(level, EntitySpawnReason.COMMAND);
        var at = origin.offset(position(input.getAsJsonArray("offset")));
        entity.snapTo(at.getX() + 0.5, at.getY(), at.getZ() + 0.5, 0, 0);
        if (entity instanceof Mob mob) {
            mob.setNoAi(true);
            mob.setPersistenceRequired();
            mob.setYHeadRot(0);
            mob.setYBodyRot(0);
            if (input.has("canPickUpLoot"))
                mob.setCanPickUpLoot(input.get("canPickUpLoot").getAsBoolean());
        }

        if (entity instanceof AbstractHorse horse && input.has("tamed"))
            horse.setTamed(input.get("tamed").getAsBoolean());

        level.addFreshEntity(entity);
    }

    // Runs one game tick and waits until the server has finished it.
    private static void step(ServerLevel level) throws Exception {
        long before = server.submit(level::getGameTime).join();
        server.submit(() -> server.tickRateManager().stepGameIfPaused(1)).join();
        while (server.submit(level::getGameTime).join() == before) {
            if (!server.isRunning())
                throw new IllegalStateException("The server stopped.");

            Thread.sleep(1);
        }
    }

    private static JsonObject checkpoint(ServerLevel level, JsonObject input, int tick, List<BlockState> before,
        List<Packet<?>> heard, List<String> gameEvents) {
        var checkpoint = new JsonObject();
        checkpoint.addProperty("tick", tick);
        var changes = changes(level, before);
        if (!changes.isEmpty())
            checkpoint.addProperty("changes", changes);

        add(checkpoint, "items", items(level));
        var drops = input.has("drops") ? input.get("drops").getAsString() : "counts";
        add(checkpoint, "entities", entities(level, drops));

        var sounds = new ArrayList<String>();
        var levelEvents = new ArrayList<String>();
        var particles = new ArrayList<String>();
        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                addSound(sounds, sound.getSound().value().location().toString());
            } else if (packet instanceof ClientboundSoundEntityPacket sound) {
                addSound(sounds, sound.getSound().value().location().toString());
            } else if (packet instanceof ClientboundLevelEventPacket event) {
                levelEvents.add(event.getType() + " " + offset(event.getPos()) + " " + event.getData());
            } else if (packet instanceof ClientboundLevelParticlesPacket particle) {
                var type = BuiltInRegistries.PARTICLE_TYPE.getKey(particle.particle().getType());
                particles.add(type + " " + particle.count());
            }
        }

        add(checkpoint, "sounds", sounds);
        add(checkpoint, "levelEvents", levelEvents);
        add(checkpoint, "particles", particles);
        add(checkpoint, "gameEvents", new ArrayList<>(gameEvents));
        if (input.has("details") && input.get("details").getAsBoolean()) {
            add(checkpoint, "details", details(level, heard));
            var motions = new ArrayList<String>();
            for (var entity : level.getEntities((Entity) null, area(EntityReach), DispenserFixtures::notPlayer)) {
                if (entity instanceof Projectile) {
                    var type = BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType());
                    var motion = entity.getDeltaMovement();
                    motions.add(type + " " + motion.x + " " + motion.y + " " + motion.z);
                }
            }
            motions.sort(String::compareTo);
            add(checkpoint, "flightMotion", motions);
        }

        return checkpoint;
    }

    // Opt-in payloads keep older captures unchanged. Only random flight coordinates, random sound pitch/seed,
    // and the random horizontal TNT impulse are omitted; stationary and vertical TNT velocities are exact.
    private static List<String> details(ServerLevel level, List<Packet<?>> heard) {
        var result = new ArrayList<String>();
        if (level.getBlockEntity(origin) instanceof DispenserBlockEntity dispenser) {
            for (int slot = 0; slot < dispenser.getContainerSize(); slot++) {
                var item = dispenser.getItem(slot);
                if (!item.isEmpty())
                    result.add("slot " + slot + " " + detailedStack(level, item));
            }
        }

        var drops = new java.util.TreeMap<String, Integer>();
        for (var entity : level.getEntities((Entity) null, area(EntityReach), DispenserFixtures::notPlayer)) {
            var type = BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString();
            if (entity instanceof AbstractArrow arrow) {
                result.add(type + " pickup=" + arrow.pickup.name().toLowerCase(Locale.ROOT)
                    + " item=" + detailedStack(level, arrow.getPickupItemStackOrigin()));
            } else if (entity instanceof ItemSupplier supplier) {
                result.add(type + " item=" + detailedStack(level, supplier.getItem()));
            } else if (entity instanceof ItemEntity item) {
                var single = item.getItem().copyWithCount(1);
                drops.merge(detailedStack(level, single), item.getItem().getCount(), Integer::sum);
            }

            if (entity instanceof FireworkRocketEntity rocket) {
                result.add(type + " angled=" + rocket.isShotAtAngle());
                var fireworks = rocket.getItem().get(net.minecraft.core.component.DataComponents.FIREWORKS);
                result.add(type + " flight=" + (fireworks == null ? 0 : fireworks.flightDuration()));
            }

            var motion = entity.getDeltaMovement();
            if (entity instanceof PrimedTnt) {
                result.add(type + " velocityY=" + number(motion.y));
            } else if (!(entity instanceof Projectile) && !(entity instanceof ItemEntity)) {
                result.add(type + " velocity=" + vector(motion.x, motion.y, motion.z));
            }

            if (entity instanceof Mob mob) {
                for (var slot : Slots) {
                    if (!mob.getItemBySlot(slot).isEmpty())
                        result.add(type + " " + slot.getName() + "=" + detailedStack(level, mob.getItemBySlot(slot)));
                }
            }
        }

        for (var entry : drops.entrySet())
            result.add("drop " + entry.getKey() + " total=" + entry.getValue());

        for (var pos : compared()) {
            if (!(level.getBlockEntity(pos) instanceof net.minecraft.world.level.block.entity.ShulkerBoxBlockEntity box))
                continue;

            for (int slot = 0; slot < box.getContainerSize(); slot++) {
                if (!box.getItem(slot).isEmpty())
                    result.add("box " + offset(pos) + " slot " + slot + " " + detailedStack(level, box.getItem(slot)));
            }
        }

        for (var packet : heard) {
            if (packet instanceof ClientboundSoundPacket sound) {
                var name = sound.getSound().value().location().toString();
                if (name.endsWith(".ambient"))
                    continue;

                // Projectile hit positions and their pitch vary with spread; category and volume still compare.
                var projectileHit = name.equals("minecraft:entity.arrow.hit");
                var flight = projectileHit || name.equals("minecraft:entity.firework_rocket.launch");
                var at = flight ? "flight" : vector(sound.getX() - origin.getX(),
                    sound.getY() - origin.getY(), sound.getZ() - origin.getZ());
                var pitch = projectileHit ? "random" : number(sound.getPitch());
                result.add("sound " + name + " " + sound.getSource().getName() + " " + at
                    + " volume=" + number(sound.getVolume()) + " pitch=" + pitch);
            } else if (packet instanceof ClientboundSoundEntityPacket sound) {
                var name = sound.getSound().value().location().toString();
                if (!name.endsWith(".ambient"))
                    result.add("entitySound " + name + " " + sound.getSource().getName()
                        + " volume=" + number(sound.getVolume()) + " pitch=" + number(sound.getPitch()));
            } else if (packet instanceof ClientboundLevelParticlesPacket particle) {
                var type = BuiltInRegistries.PARTICLE_TYPE.getKey(particle.particle().getType());
                result.add("particle " + type + " " + vector(particle.x() - origin.getX(),
                    particle.y() - origin.getY(), particle.z() - origin.getZ())
                    + " count=" + particle.count());
            }
        }

        result.sort(String::compareTo);
        return result;
    }

    private static String detailedStack(ServerLevel level, ItemStack item) {
        var text = stack(item);
        if (item.getComponentsPatch().isEmpty())
            return text;

        var ops = level.registryAccess().createSerializationContext(net.minecraft.nbt.NbtOps.INSTANCE);
        var patch = net.minecraft.core.component.DataComponentPatch.CODEC
            .encodeStart(ops, item.getComponentsPatch()).getOrThrow();
        return text + " " + patch;
    }

    private static String number(double value) {
        return String.format(Locale.ROOT, "%.6f", value == 0 ? 0.0 : value);
    }

    private static String vector(double x, double y, double z) {
        return number(x) + "," + number(y) + "," + number(z);
    }

    // Sounds at a position and sounds following an entity alike, by name.
    private static void addSound(List<String> sounds, String name) {
        if (!name.endsWith(".ambient"))
            sounds.add(name);
    }

    private static void add(JsonObject checkpoint, String name, List<String> values) {
        if (values.isEmpty())
            return;

        var array = new JsonArray();
        for (var value : values)
            array.add(value);

        checkpoint.add(name, array);
    }

    // The dispenser's items as "<slot> <id> x<count>", with their damage when they have any.
    private static List<String> items(ServerLevel level) {
        var items = new ArrayList<String>();
        if (!(level.getBlockEntity(origin) instanceof DispenserBlockEntity dispenser))
            return items;

        for (int slot = 0; slot < dispenser.getContainerSize(); slot++) {
            var stack = dispenser.getItem(slot);
            if (!stack.isEmpty())
                items.add(slot + " " + stack(stack));
        }

        return items;
    }

    // The entities in the area, sorted. Dropped items are summed by item, since item entities merge: "item <id>
    // x<total>", or where the case's drops are random only "item <id>" ("ids") or "item" ("none"). Primed TNT shows
    // its fuse, and other entities their type, equipment and whether they can be sheared.
    private static List<String> entities(ServerLevel level, String drops) {
        var found = new ArrayList<String>();
        var totals = new java.util.TreeMap<String, Integer>();
        for (var entity : level.getEntities((Entity) null, area(EntityReach), DispenserFixtures::notPlayer)) {
            if (entity instanceof ItemEntity item) {
                var id = BuiltInRegistries.ITEM.getKey(item.getItem().getItem()).toString();
                totals.merge(id, item.getItem().getCount(), Integer::sum);
            } else {
                found.add(describe(entity));
            }
        }

        for (var total : totals.entrySet()) {
            var text = switch (drops) {
                case "ids" -> "item " + total.getKey();
                case "none" -> "item";
                default -> "item " + total.getKey() + " x" + total.getValue();
            };

            if (!found.contains(text))
                found.add(text);
        }

        found.sort(String::compareTo);
        return found;
    }

    private static String describe(Entity entity) {
        var text = new StringBuilder(BuiltInRegistries.ENTITY_TYPE.getKey(entity.getType()).toString());
        if (entity instanceof PrimedTnt tnt)
            text.append(" fuse=").append(tnt.getFuse());

        if (entity instanceof Mob mob) {
            for (var slot : Slots) {
                var stack = mob.getItemBySlot(slot);
                var item = BuiltInRegistries.ITEM.getKey(stack.getItem());
                if (!stack.isEmpty())
                    text.append(' ').append(slot.getName()).append('=').append(item);
            }
        }

        if (entity instanceof Shearable shearable)
            text.append(" shearable=").append(shearable.readyForShearing());

        return text.toString();
    }

    private static void discardEntities(ServerLevel level) {
        for (var entity : level.getEntities((Entity) null, area(EntityReach + 2), DispenserFixtures::notPlayer))
            entity.discard();
    }

    private static boolean notPlayer(Entity entity) {
        return !(entity instanceof ServerPlayer);
    }

    private static AABB area(int reach) {
        return new AABB(origin).inflate(reach);
    }

    // An item stack as "<id> x<count>", with its damage when it has any.
    private static String stack(ItemStack stack) {
        var text = BuiltInRegistries.ITEM.getKey(stack.getItem()) + " x" + stack.getCount();
        return stack.getDamageValue() > 0 ? text + " damage=" + stack.getDamageValue() : text;
    }

    private static String offset(BlockPos pos) {
        var relative = pos.subtract(origin);
        return relative.getX() + "," + relative.getY() + "," + relative.getZ();
    }

    private static Direction facing(JsonObject input) {
        return Direction.valueOf(input.get("facing").getAsString().toUpperCase(Locale.ROOT));
    }

    private static GameProfile profile(String name) {
        return new GameProfile(UUID.nameUUIDFromBytes(name.getBytes(StandardCharsets.UTF_8)), name);
    }

    @SuppressWarnings("unchecked")
    private static List<ServerPlayer> players(PlayerList list) throws Exception {
        var field = PlayerList.class.getDeclaredField("players");
        field.setAccessible(true);
        return (List<ServerPlayer>) field.get(list);
    }

    // A game connection that keeps the packets sent to it instead of sending them.
    private static ServerGamePacketListenerImpl recorder(List<Packet<?>> packets) throws Exception {
        var unsafeField = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var allocate = unsafe.getClass().getMethod("allocateInstance", Class.class);
        var recorder = (Recorder) allocate.invoke(unsafe, Recorder.class);
        recorder.packets = packets;

        // The server ticks with this player in its list, which sends it chunks: it has none pending.
        var chunkSender = ServerGamePacketListenerImpl.class.getDeclaredField("chunkSender");
        var offset = (long) unsafe.getClass().getMethod("objectFieldOffset", java.lang.reflect.Field.class)
            .invoke(unsafe, chunkSender);
        unsafe.getClass().getMethod("putObject", Object.class, long.class, Object.class)
            .invoke(unsafe, recorder, offset, new net.minecraft.server.network.PlayerChunkSender(false));
        return recorder;
    }

    private static void clear(ServerLevel level) {
        var air = Blocks.AIR.defaultBlockState();
        for (var pos : BlockPos.betweenClosed(origin.offset(-Reach - 1, -Reach - 1, -Reach - 1),
            origin.offset(Reach + 1, Reach + 1, Reach + 1))) {
            level.setBlock(pos, air, QuietFlags);
        }
    }

    private static List<BlockState> snapshot(ServerLevel level) {
        var states = new ArrayList<BlockState>();
        for (var pos : compared())
            states.add(level.getBlockState(pos));

        return states;
    }

    private static String changes(ServerLevel level, List<BlockState> before) {
        var changed = new ArrayList<String>();
        int index = 0;
        for (var pos : compared()) {
            var state = level.getBlockState(pos);
            if (state != before.get(index))
                changed.add(offset(pos) + "=" + text(state));

            index++;
        }

        return String.join(";", changed);
    }

    // The compared area in a fixed order: y, then z, then x ascending.
    private static List<BlockPos> compared() {
        var positions = new ArrayList<BlockPos>();
        for (int y = -Reach; y <= Reach; y++) {
            for (int z = -Reach; z <= Reach; z++) {
                for (int x = -Reach; x <= Reach; x++)
                    positions.add(origin.offset(x, y, z));
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

        // A flat world of air in plains, with nothing spawning, no random ticks and no watchdog.
        var properties = String.join("\n",
            "level-type=minecraft\\:flat",
            "generator-settings={\"layers\"\\:[{\"block\"\\:\"minecraft\\:air\",\"height\"\\:1}],"
                + "\"biome\"\\:\"minecraft\\:plains\"}",
            "generate-structures=false",
            "spawn-protection=0",
            "spawn-monsters=false",
            "spawn-animals=false",
            "difficulty=normal",
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

        // Random ticks would grow and decay the cases' plants on their own, and a snow golem would lay snow.
        for (var command : List.of("gamerule random_tick_speed 0", "gamerule mob_griefing false")) {
            server.submit(() -> server.getCommands().performPrefixedCommand(
                server.createCommandSourceStack(), command)).join();
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

        // The server suspends and resumes each player's flushing around its tick; there's nothing to flush.
        @Override public void suspendFlushing() {
        }

        @Override public void resumeFlushing() {
        }
    }

    /** Records every game event within 16 blocks of the dispenser, by name, offset and source. */
    private static final class Listener implements GameEventListener {
        private final List<String> events;

        Listener(List<String> events) {
            this.events = events;
        }

        @Override public PositionSource getListenerSource() {
            return new BlockPositionSource(origin);
        }

        @Override public int getListenerRadius() {
            return 16;
        }

        @Override public boolean handleGameEvent(ServerLevel level, Holder<GameEvent> event, GameEvent.Context context,
            Vec3 pos) {
            var name = event.unwrapKey().orElseThrow().identifier().toString();
            var source = context.sourceEntity() == null
                ? "none"
                : BuiltInRegistries.ENTITY_TYPE.getKey(context.sourceEntity().getType()).toString();
            events.add(name + " " + offset(BlockPos.containing(pos)) + " " + source);
            return true;
        }
    }
}
