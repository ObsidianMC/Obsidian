import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import net.minecraft.core.BlockPos;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.resources.Identifier;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.util.RandomSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.effect.MobEffects;
import net.minecraft.world.entity.decoration.ArmorStand;
import net.minecraft.world.entity.item.FallingBlockEntity;
import net.minecraft.world.entity.projectile.arrow.ThrownTrident;
import net.minecraft.world.phys.BlockHitResult;
import net.minecraft.core.Direction;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.context.DirectionalPlaceContext;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.damagesource.DamageSources;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.PotentSulfurBlockEntity;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.Property;
import net.minecraft.world.level.entity.EntityTypeTest;
import net.minecraft.world.level.material.FluidState;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.entity.MoverType;
import net.minecraft.world.level.ItemLike;
import net.minecraft.world.level.gamerules.GameRules;

/** Calls vanilla's sulfur growth and gas/launch routines on a bounded, explicitly supplied block view. */
public final class SulfurFixtures {
    public static void main(String[] arguments) throws Exception {
        var version = VanillaDumper.bootstrap();
        var root = JsonParser.parseString(Files.readString(Path.of(arguments[0]))).getAsJsonObject();
        root.addProperty("version", version);
        var selected = arguments.length > 1 ? Set.of(arguments[1].split("\\+")) : null;
        int captured = 0;
        for (var entry : root.getAsJsonArray("cases")) {
            var input = entry.getAsJsonObject();
            if (selected != null && !selected.contains(input.get("name").getAsString())) {
                continue;
            }

            captured++;
            for (var key : List.of("writes", "nextRandom", "source", "forceHeight", "reaches",
                "launchedMotionY", "nauseaDuration", "ambient", "particles", "damage", "broken", "placed",
                "remainingFallDistance", "clampedFallDistance")) {
                input.remove(key);
            }
            var level = allocateLevel();
            for (var placement : input.getAsJsonArray("setup")) {
                var value = placement.getAsJsonObject();
                level.blocks.put(position(value.getAsJsonArray("pos")), state(value.get("state").getAsString()));
            }

            var origin = position(input.getAsJsonArray("origin"));
            if (input.get("kind").getAsString().equals("shape_update")) {
                var ticks = new JsonArray();
                var blockTicks = java.lang.reflect.Proxy.newProxyInstance(
                    SulfurFixtures.class.getClassLoader(),
                    new Class<?>[] { net.minecraft.world.ticks.LevelTickAccess.class },
                    (proxy, method, values) -> method.getReturnType() == boolean.class ? false : null);
                var scheduler = (net.minecraft.world.level.ScheduledTickAccess)java.lang.reflect.Proxy.newProxyInstance(
                    SulfurFixtures.class.getClassLoader(),
                    new Class<?>[] { net.minecraft.world.level.ScheduledTickAccess.class },
                    (proxy, method, values) -> {
                        if (method.getName().equals("getBlockTicks")) {
                            return blockTicks;
                        }

                        if (method.getName().equals("scheduleTick")
                            && values[1] instanceof net.minecraft.world.level.material.Fluid fluid) {
                            var tick = new JsonObject();
                            tick.add("pos", coordinates((BlockPos)values[0]));
                            tick.addProperty("type", BuiltInRegistries.FLUID.getKey(fluid).toString());
                            tick.addProperty("delay", (int)values[2]);
                            ticks.add(tick);
                        }

                        return null;
                    });
                var neighbor = position(input.getAsJsonArray("neighbor"));
                var direction = Direction.valueOf(input.get("direction").getAsString()
                    .toUpperCase(java.util.Locale.ROOT));
                var updated = level.getBlockState(origin).updateShape(level, scheduler, origin, direction,
                    neighbor, level.getBlockState(neighbor), RandomSource.create(1));
                input.addProperty("updated", updated.toString().replace("Block{", "").replace("}", ""));
                input.add("fluidTicks", ticks);
            } else if (input.get("kind").getAsString().equals("landing")) {
                level.rules.set(GameRules.ENTITY_DROPS, input.get("entityDrops").getAsBoolean(), null);
                var falling = new LandingProbe(level);
                falling.setPos(origin.getX() + 0.5, origin.getY(), origin.getZ() + 0.5);
                var blockState = FallingBlockEntity.class.getDeclaredField("blockState");
                blockState.setAccessible(true);
                blockState.set(falling, state("minecraft:sulfur_spike[vertical_direction=down]"));
                var cancelDrop = FallingBlockEntity.class.getDeclaredField("cancelDrop");
                cancelDrop.setAccessible(true);
                cancelDrop.setBoolean(falling, input.get("cancelDrop").getAsBoolean());
                falling.dropItem = input.get("dropItem").getAsBoolean();
                falling.setSilent(input.get("silent").getAsBoolean());
                falling.tick();
                input.addProperty("removed", falling.isRemoved());
                input.addProperty("items", falling.items);
                input.add("events", level.events);
            } else if (input.get("kind").getAsString().equals("placement")) {
                var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(input.get("block").getAsString()));
                var look = Direction.valueOf(input.get("look").getAsString().toUpperCase(java.util.Locale.ROOT));
                var face = Direction.valueOf(input.get("face").getAsString().toUpperCase(java.util.Locale.ROOT));
                var context = new PlacementContext(level, origin, new ItemStack(block), look, face,
                    input.get("secondary").getAsBoolean());
                var placed = block.getStateForPlacement(context);
                input.addProperty("placed", placed == null ? null
                    : placed.toString().replace("Block{", "").replace("}", ""));
            } else if (input.get("kind").getAsString().equals("projectile")) {
                var trident = new ProbeTrident(level, input.get("allowed").getAsBoolean());
                trident.setDeltaMovement(0, 0, input.get("speed").getAsDouble());
                var spike = Blocks.SULFUR_SPIKE.defaultBlockState();
                spike.onProjectileHit(level, spike,
                    new BlockHitResult(Vec3.atCenterOf(origin), Direction.NORTH, origin, false), trident);
                input.addProperty("broken", level.broken);
            } else if (input.get("kind").getAsString().equals("impact")) {
                var victim = new ImpactTarget(level);
                victim.setPos(origin.getX() + 0.5, origin.getY(), origin.getZ() + 0.5);
                level.entities.add(victim);
                var falling = FallingBlockEntity.fall(level, origin, Blocks.SULFUR_SPIKE.defaultBlockState());
                falling.setHurtsEntities(input.get("size").getAsFloat(), 40);
                falling.causeFallDamage(input.get("distance").getAsDouble(), 1, null);
                input.addProperty("damage", victim.damage);
            } else if (input.get("kind").getAsString().equals("growth")) {
                var random = RandomSource.create(input.get("seed").getAsLong());
                level.getBlockState(origin).randomTick(level, origin, random);
                var writes = new JsonArray();
                for (var write : level.writes) {
                    writes.add(write);
                }

                input.add("writes", writes);
                input.addProperty("nextRandom", random.nextInt());
            } else {
                var find = PotentSulfurBlockEntity.class.getDeclaredMethod(
                    "findNoxiousGasSourceBlock", net.minecraft.world.level.Level.class, BlockPos.class);
                find.setAccessible(true);
                var source = (BlockPos)find.invoke(null, level, origin);
                input.add("source", source == null ? null : coordinates(source));
                if (source != null) {
                    var depth = source.getY() - origin.getY() - 1;
                    var force = PotentSulfurBlockEntity.class.getDeclaredMethod(
                        "getUnobstructedBlockCount", net.minecraft.world.level.Level.class, BlockPos.class, int.class);
                    force.setAccessible(true);
                    input.addProperty("forceHeight", (int)force.invoke(null, level, origin.above(), depth));
                    var reaches = new JsonArray();
                    for (var eye : input.getAsJsonArray("eyes")) {
                        var point = eye.getAsJsonArray();
                        reaches.add(PotentSulfurBlockEntity.canBeReachedByNoxiousGas(level, source,
                            new Vec3(point.get(0).getAsDouble(), point.get(1).getAsDouble(),
                                point.get(2).getAsDouble())));
                    }

                    input.add("reaches", reaches);
                    if (input.has("entity")) {
                        var actor = input.getAsJsonObject("entity");
                        var stand = new ArmorStand(EntityTypes.ARMOR_STAND, level);
                        var pos = actor.getAsJsonArray("pos");
                        stand.setPos(pos.get(0).getAsDouble(), pos.get(1).getAsDouble(), pos.get(2).getAsDouble());
                        stand.setDeltaMovement(0, actor.get("motionY").getAsDouble(), 0);
                        stand.fallDistance = 10;
                        level.entities.add(stand);
                        PotentSulfurBlockEntity.LAUNCH_ENTITY_TICKER.tick(level, origin,
                            Blocks.POTENT_SULFUR.defaultBlockState(),
                            new PotentSulfurBlockEntity(origin, Blocks.POTENT_SULFUR.defaultBlockState()));
                        input.addProperty("launchedMotionY", stand.getDeltaMovement().y);
                        input.addProperty("remainingFallDistance", stand.fallDistance);
                        stand.fallDistance = 10;
                        stand.setDeltaMovement(0, actor.get("motionY").getAsDouble(), 0);
                        stand.checkFallDistanceAccumulation();
                        input.addProperty("clampedFallDistance", stand.fallDistance);
                        PotentSulfurBlockEntity.SERVER_NAUSEA_EFFECT_TICKER.tick(level, origin,
                            Blocks.POTENT_SULFUR.defaultBlockState(),
                            new PotentSulfurBlockEntity(origin, Blocks.POTENT_SULFUR.defaultBlockState()));
                        var effect = stand.getEffect(MobEffects.NAUSEA);
                        input.addProperty("nauseaDuration", effect == null ? 0 : effect.getDuration());
                        if (effect != null) {
                            input.addProperty("ambient", effect.isAmbient());
                            input.addProperty("particles", effect.isVisible());
                        }
                    }
                }
            }
        }

        if (selected != null && captured != selected.size()) {
            throw new IllegalArgumentException("Every selected sulfur probe must exist and be named once");
        }

        Files.writeString(Path.of(arguments[0]),
            new GsonBuilder().serializeNulls().setPrettyPrinting().disableHtmlEscaping().create().toJson(root) + "\n");
    }

    private static ProbeLevel allocateLevel() throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        var level = (ProbeLevel)unsafe.getClass().getMethod("allocateInstance", Class.class)
            .invoke(unsafe, ProbeLevel.class);
        level.blocks = new HashMap<>();
        level.writes = new ArrayList<>();
        level.entities = new ArrayList<>();
        level.rules = new GameRules(List.of(GameRules.ENTITY_DROPS));
        level.events = new JsonArray();
        return level;
    }

    private static BlockPos position(JsonArray values) {
        return new BlockPos(values.get(0).getAsInt(), values.get(1).getAsInt(), values.get(2).getAsInt());
    }

    private static JsonArray coordinates(BlockPos position) {
        var result = new JsonArray();
        result.add(position.getX());
        result.add(position.getY());
        result.add(position.getZ());
        return result;
    }

    private static BlockState state(String text) {
        var bracket = text.indexOf('[');
        var name = bracket < 0 ? text : text.substring(0, bracket);
        var block = BuiltInRegistries.BLOCK.getValue(Identifier.parse(name));
        var state = block.defaultBlockState();
        if (bracket >= 0) {
            for (var pair : text.substring(bracket + 1, text.length() - 1).split(",")) {
                var parts = pair.split("=");
                var property = block.getStateDefinition().getProperty(parts[0]);
                state = withProperty(state, property, parts[1]);
            }
        }

        return state;
    }

    private static <T extends Comparable<T>> BlockState withProperty(BlockState state, Property<T> property,
        String value) {
        return state.setValue(property, property.getValue(value).orElseThrow());
    }

    private static final class ProbeLevel extends ServerLevel {
        Map<BlockPos, BlockState> blocks;
        List<JsonObject> writes;
        List<Entity> entities;
        boolean broken;
        GameRules rules;
        JsonArray events;

        @Override public GameRules getGameRules() {
            return rules;
        }

        @Override public void levelEvent(Entity source, int event, BlockPos pos, int data) {
            var recorded = new JsonObject();
            recorded.addProperty("type", event);
            recorded.add("pos", coordinates(pos));
            recorded.addProperty("data", data);
            events.add(recorded);
        }

        @Override public boolean destroyBlock(BlockPos pos, boolean drops, Entity breaker, int limit) {
            broken = true;
            return true;
        }

        @Override public DamageSources damageSources() {
            try {
                return (DamageSources)allocate(ProbeDamageSources.class);
            } catch (Exception exception) {
                throw new RuntimeException(exception);
            }
        }

        @Override public boolean addFreshEntity(Entity entity) {
            return true;
        }

        @Override public List<Entity> getEntities(Entity except, AABB box,
            java.util.function.Predicate<? super Entity> predicate) {
            return entities.stream().filter(entity -> entity != except)
                .filter(entity -> box.intersects(entity.getBoundingBox())).filter(predicate).toList();
        }

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
            throw new UnsupportedOperationException();
        }

        @Override public BlockState getBlockState(BlockPos pos) {
            return blocks.getOrDefault(pos, Blocks.AIR.defaultBlockState());
        }

        @Override public int getNextEntityId() {
            return 1;
        }

        @Override public long getGameTime() {
            return 20;
        }

        @Override public FluidState getFluidState(BlockPos pos) {
            return getBlockState(pos).getFluidState();
        }

        @Override public int getMinY() {
            return -64;
        }

        @Override public int getHeight() {
            return 384;
        }

        @Override public boolean setBlock(BlockPos pos, BlockState state, int flags, int limit) {
            blocks.put(pos.immutable(), state);
            var write = new JsonObject();
            write.add("pos", coordinates(pos));
            write.addProperty("state", state.toString().replace("Block{", "").replace("}", ""));
            writes.add(write);
            return true;
        }

        @Override public <T extends Entity> List<T> getEntities(EntityTypeTest<Entity, T> test, AABB box,
            java.util.function.Predicate<? super T> predicate) {
            var result = new ArrayList<T>();
            for (var entity : entities) {
                var match = test.tryCast(entity);
                if (match != null && box.intersects(entity.getBoundingBox()) && predicate.test(match)) {
                    result.add(match);
                }
            }

            return result;
        }
    }

    private static Object allocate(Class<?> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type);
    }

    private static final class ProbeDamageSources extends DamageSources {
        private ProbeDamageSources() {
            super(null);
        }

        @Override public DamageSource fallingStalactite(Entity entity) {
            return null;
        }
    }

    private static final class ImpactTarget extends ArmorStand {
        float damage;

        ImpactTarget(ProbeLevel level) {
            super(EntityTypes.ARMOR_STAND, level);
        }

        @Override public boolean hurtServer(ServerLevel level, DamageSource source, float amount) {
            damage += amount;
            return true;
        }
    }

    private static final class ProbeTrident extends ThrownTrident {
        private final boolean allowed;

        ProbeTrident(ProbeLevel level, boolean allowed) {
            super(EntityTypes.TRIDENT, level);
            this.allowed = allowed;
        }

        @Override public boolean mayInteract(ServerLevel level, BlockPos pos) {
            return allowed;
        }

        @Override public boolean mayBreak(ServerLevel level, BlockPos pos) {
            return allowed;
        }
    }

    private static final class PlacementContext extends DirectionalPlaceContext {
        private final Direction look;
        private final boolean secondary;

        PlacementContext(ProbeLevel level, BlockPos pos, ItemStack stack, Direction look, Direction face,
            boolean secondary) {
            super(level, pos, Direction.NORTH, stack, face);
            this.look = look;
            this.secondary = secondary;
        }

        @Override public Direction getNearestLookingVerticalDirection() {
            return look;
        }

        @Override public boolean isSecondaryUseActive() {
            return secondary;
        }
    }

    /** Supplies an already resolved ground contact; vanilla still decides removal, drops and break effects. */
    private static final class LandingProbe extends FallingBlockEntity {
        int items;

        LandingProbe(ProbeLevel level) {
            super(EntityTypes.FALLING_BLOCK, level);
        }

        @Override public void move(MoverType type, Vec3 delta) {
            setOnGround(true);
        }

        @Override protected void checkSupportingBlock(boolean onGround, Vec3 movement) {
        }

        @Override protected void applyEffectsFromBlocks() {
        }

        @Override protected void handlePortal() {
        }

        @Override protected void applyGravity() {
        }

        @Override protected float getAirDrag() {
            return 1;
        }

        @Override public net.minecraft.world.entity.item.ItemEntity spawnAtLocation(ServerLevel level, ItemLike item) {
            items++;
            return null;
        }
    }
}
