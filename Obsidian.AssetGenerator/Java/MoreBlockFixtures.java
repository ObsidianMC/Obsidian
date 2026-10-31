import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Direction;
import net.minecraft.core.Holder;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.sounds.SoundEvent;
import net.minecraft.sounds.SoundSource;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.context.DirectionalPlaceContext;
import net.minecraft.world.level.GameType;
import net.minecraft.world.level.Level;
import net.minecraft.world.level.ScheduledTickAccess;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.NoteBlock;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.world.phys.Vec3;


/** Records construction-family state transitions and the note block's actual instrument and sound routines. */
public final class MoreBlockFixtures {
    public static void main(String[] args) throws Exception {
        var version = VanillaDumper.bootstrap();
        var root = JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonObject();
        root.addProperty("version", version);
        var selected = args.length > 2 ? Set.of(args[2].split("\\+")) : null;
        int captured = 0;
        for (var entry : root.getAsJsonArray("cases")) {
            var input = entry.getAsJsonObject();
            if (selected != null && !selected.contains(input.get("name").getAsString())) {
                continue;
            }

            captured++;
            var level = createLevel();
            for (var setup : input.getAsJsonArray("setup")) {
                var placement = setup.getAsJsonObject();
                level.blocks.put(position(placement.getAsJsonArray("pos")),
                    state(placement.get("state").getAsString()));
            }

            var origin = position(input.getAsJsonArray("origin"));
            var random = RandomSource.create(input.has("seed") ? input.get("seed").getAsLong() : 123);
            level.random = random;
            level.powered = input.has("powered") && input.get("powered").getAsBoolean();
            var kind = input.get("kind").getAsString();
            if (kind.equals("placement")) {
                var block = state(input.get("block").getAsString()).getBlock();
                var context = new PlacementContext(level, origin,
                    direction(input.get("look").getAsString()), new ItemStack(block),
                    direction(input.get("face").getAsString()));
                var placed = block.getStateForPlacement(context);
                if (placed != null && !placed.canSurvive(level, origin)) {
                    placed = null;
                }

                input.addProperty("rotation", context.getRotation());
                input.addProperty("replacesClicked", context.replacingClickedOnBlock());
                var cursor = new JsonArray();
                cursor.add(context.getClickLocation().x - origin.getX());
                cursor.add(context.getClickLocation().y - origin.getY());
                cursor.add(context.getClickLocation().z - origin.getZ());
                input.add("cursor", cursor);
                input.addProperty("horizontal", context.getHorizontalDirection().getSerializedName());
                input.addProperty("nearest", context.getNearestLookingDirection().getSerializedName());
                input.addProperty("placed", placed == null ? null : text(placed));
            } else if (kind.equals("shape")) {
                var side = direction(input.get("direction").getAsString());
                var neighbor = origin.relative(side);
                var updated = level.getBlockState(origin).updateShape(level, level.scheduler(), origin, side,
                    neighbor, level.getBlockState(neighbor), random);
                input.addProperty("updated", text(updated));
                input.add("ticks", level.ticks);
            } else if (kind.equals("leaf_tick")) {
                level.getBlockState(origin).tick(level, origin, random);
                input.addProperty("updated", text(level.getBlockState(origin)));
                input.addProperty("nextRandom", random.nextInt());
            } else if (kind.equals("strip")) {
                var transformer = net.minecraft.world.item.Items.IRON_AXE.getDefaultInstance()
                    .get(DataComponents.BLOCK_TRANSFORMER).value();
                BlockState transformed = null;
                for (var transform : transformer.transforms()) {
                    var candidate = transform.blockStateProvider().value().getOptionalState(level, random, origin);
                    if (candidate != null) {
                        transformed = candidate;
                        input.addProperty("sound", transform.sound().value().location().toString());
                        input.addProperty("damage", transform.itemDamagePerUse());
                        break;
                    }
                }

                input.addProperty("transformed", transformed == null ? null : text(transformed));
                input.addProperty("nextRandom", random.nextInt());
            } else if (kind.equals("use")) {
                var player = (ProbePlayer)allocate(ProbePlayer.class);
                player.setYRot(input.get("rotation").getAsFloat());
                var before = level.getBlockState(origin);
                before.useWithoutItem(level, player,
                    new BlockHitResult(Vec3.atCenterOf(origin), Direction.UP, origin, false));
                input.addProperty("updated", text(level.getBlockState(origin)));
                input.add("sounds", level.sounds);
            } else if (kind.equals("placed_by")) {
                var placed = level.getBlockState(origin);
                placed.getBlock().setPlacedBy(level, origin, placed, null, new ItemStack(placed.getBlock()));
                input.addProperty("upper", text(level.getBlockState(origin.above())));
            } else if (kind.equals("note")) {
                var note = level.getBlockState(origin);
                var play = NoteBlock.class.getDeclaredMethod("playNote", Entity.class, BlockState.class,
                    Level.class, BlockPos.class);
                play.setAccessible(true);
                play.invoke(Blocks.NOTE_BLOCK, null, note, level, origin);
                boolean triggered = !level.events.isEmpty() && note.triggerEvent(level, origin, 0, 0);
                input.addProperty("triggered", triggered);

                input.add("events", level.events);
                input.add("sounds", level.sounds);
                input.addProperty("nextRandom", random.nextInt());
            } else {
                throw new IllegalArgumentException("Unknown probe kind: " + kind);
            }
        }

        if (selected != null && captured != selected.size()) {
            throw new IllegalArgumentException("Every selected case must exist and be named once.");
        }

        var gson = new GsonBuilder().serializeNulls().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(Path.of(args[0]), gson.toJson(root) + "\n");
        if (!args[1].equals("-")) {
            NoteBlockInstruments.dumpInstruments(Path.of(args[1]));
        }
    }

    private static ProbeLevel createLevel() throws Exception {
        var level = (ProbeLevel)allocate(ProbeLevel.class);
        level.blocks = new HashMap<>();
        level.ticks = new JsonArray();
        level.events = new JsonArray();
        level.sounds = new JsonArray();
        return level;
    }

    private static Object allocate(Class<?> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type);
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static Direction direction(String name) {
        return Direction.valueOf(name.toUpperCase(java.util.Locale.ROOT));
    }

    private static String text(BlockState state) {
        return state.toString().replace("Block{", "").replace("}", "");
    }

    private static BlockState state(String text) {
        var bracket = text.indexOf('[');
        var name = bracket < 0 ? text : text.substring(0, bracket);
        var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(name));
        var state = block.defaultBlockState();
        if (bracket >= 0) {
            for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
                var parts = pair.split("=");
                state = property(state, block.getStateDefinition().getProperty(parts[0]), parts[1]);
            }
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState property(BlockState state, Property<T> property, String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    private static final class PlacementContext extends DirectionalPlaceContext {
        PlacementContext(ProbeLevel level, BlockPos pos, Direction look, ItemStack item, Direction face) {
            super(level, pos, look, item, face);
        }

        @Override public boolean replacingClickedOnBlock() {
            return true;
        }
    }

    private static final class ProbePlayer extends Player {
        private ProbePlayer() {
            super(null, null);
            throw new UnsupportedOperationException();
        }

        @Override public GameType gameMode() {
            return GameType.SURVIVAL;
        }
    }

    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        JsonArray ticks;
        JsonArray events;
        JsonArray sounds;
        RandomSource random;
        boolean powered;

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
            throw new UnsupportedOperationException();
        }

        @Override public BlockState getBlockState(BlockPos pos) {
            return blocks.getOrDefault(pos, Blocks.AIR.defaultBlockState());
        }

        @Override public FluidState getFluidState(BlockPos pos) {
            return getBlockState(pos).getFluidState();
        }

        @Override public net.minecraft.world.level.block.entity.BlockEntity getBlockEntity(BlockPos pos) {
            return null;
        }

        @Override public int getMinY() {
            return -64;
        }

        @Override public int getHeight() {
            return 384;
        }

        @Override public RandomSource getRandom() {
            return random;
        }

        @Override public boolean hasNeighborSignal(BlockPos pos) {
            return powered;
        }

        @Override public boolean setBlock(BlockPos pos, BlockState state, int flags, int limit) {
            blocks.put(pos.immutable(), state);
            return true;
        }

        @Override public void blockEvent(BlockPos pos, Block block, int id, int parameter) {
            var value = new JsonObject();
            value.addProperty("id", id);
            value.addProperty("parameter", parameter);
            events.add(value);
        }

        @Override public void gameEvent(Holder<net.minecraft.world.level.gameevent.GameEvent> event,
            net.minecraft.world.phys.Vec3 pos, net.minecraft.world.level.gameevent.GameEvent.Context context) {
        }

        @Override public void playSound(Entity source, BlockPos pos, SoundEvent sound,
            SoundSource category, float volume, float pitch) {
            var value = new JsonObject();
            value.addProperty("sound", sound.location().toString());
            value.addProperty("volume", volume);
            value.addProperty("pitch", pitch);
            sounds.add(value);
        }

        @Override public void playSeededSound(Entity source, double x, double y, double z, Holder<SoundEvent> sound,
            SoundSource category, float volume, float pitch, long seed) {
            var value = new JsonObject();
            value.addProperty("sound", sound.value().location().toString());
            value.addProperty("volume", volume);
            value.addProperty("pitch", pitch);
            value.addProperty("seed", seed);
            sounds.add(value);
        }

        ScheduledTickAccess scheduler() {
            return (ScheduledTickAccess)java.lang.reflect.Proxy.newProxyInstance(
                MoreBlockFixtures.class.getClassLoader(), new Class<?>[] { ScheduledTickAccess.class },
                (proxy, method, values) -> {
                    if (method.getName().equals("scheduleTick")) {
                        var tick = new JsonObject();
                        tick.addProperty("type", values[1] instanceof Fluid fluid
                            ? BuiltInRegistries.FLUID.getKey(fluid).toString()
                            : BuiltInRegistries.BLOCK.getKey((Block)values[1]).toString());
                        tick.addProperty("delay", (int)values[2]);
                        ticks.add(tick);
                    }

                    return null;
                });
        }
    }
}
