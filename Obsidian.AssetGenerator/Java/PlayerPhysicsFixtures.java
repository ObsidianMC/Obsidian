import com.google.gson.*;
import java.nio.file.*;
import java.lang.reflect.*;
import java.util.*;
import net.minecraft.SharedConstants;
import net.minecraft.core.BlockPos;
import net.minecraft.core.Holder;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.server.Bootstrap;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.tags.TagKey;
import net.minecraft.util.SmoothDouble;
import net.minecraft.world.effect.MobEffect;
import net.minecraft.world.effect.MobEffectInstance;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.EntityTypes;
import net.minecraft.world.entity.HumanoidArm;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.MoverType;
import net.minecraft.world.entity.ai.attributes.Attribute;
import net.minecraft.world.entity.ai.attributes.Attributes;
import net.minecraft.world.level.block.Block;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.border.WorldBorder;
import net.minecraft.world.level.material.Fluid;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;
import net.minecraft.world.phys.shapes.Shapes;
import net.minecraft.world.phys.shapes.VoxelShape;

/** Build-time physics capture using the installed client; no vanilla source is copied.
 * Unsafe skips constructors only to replace unrelated server/world services. All measured collision and
 * travel methods execute in the original jar. The output is an ignored generated parity fixture.
 */
class PlayerPhysicsFixtures {
    static sun.misc.Unsafe unsafe;
    static Method sweep, collide, air, water, lava, glide;

    static class World extends ServerLevel {
        BlockState surface;
        List<VoxelShape> shapes;

        World() {
            super(null, null, null, null, null, null, false, 0, null, false);
        }

        @Override public BlockState getBlockState(BlockPos pos) { return surface; }

        @Override public List<VoxelShape> getEntityCollisions(Entity entity, AABB box) { return List.of(); }

        @Override public Iterable<VoxelShape> getBlockCollisions(Entity entity, AABB box) { return shapes; }

        @Override public WorldBorder getWorldBorder() { return new WorldBorder(); }
    }

    static class Probe extends LivingEntity {
        boolean ground, ladder, sprint;
        Vec3 accumulated;

        Probe() {
            super(EntityTypes.PLAYER, null);
        }

        @Override public HumanoidArm getMainArm() { return HumanoidArm.RIGHT; }
        @Override public boolean onGround() { return ground; }
        @Override public boolean onClimbable() { return ladder; }
        @Override public boolean isSprinting() { return sprint; }
        @Override public float getSpeed() { return .1f; }
        @Override protected float getFlyingSpeed() { return .02f; }
        @Override protected double getEffectiveGravity() { return .08; }
        @Override public BlockPos getBlockPosBelowThatAffectsMyMovement() { return BlockPos.ZERO; }
        @Override public boolean hasEffect(Holder<MobEffect> effect) { return false; }
        @Override public MobEffectInstance getEffect(Holder<MobEffect> effect) { return null; }
        @Override public double getFluidHeight(TagKey<Fluid> tag) { return .8; }
        @Override public float maxUpStep() { return .6f; }
        @Override public boolean isSuppressingSlidingDownLadder() { return false; }

        @Override
        public double getAttributeValue(Holder<Attribute> attribute) {
            if (attribute == Attributes.JUMP_STRENGTH) {
                return .42;
            }
            if (attribute == Attributes.GRAVITY) {
                return .08;
            }
            return 0;
        }

        @Override
        public void move(MoverType type, Vec3 movement) {
            accumulated = accumulated.add(movement.x, ground ? 0 : movement.y, movement.z);
            if (ground) {
                setDeltaMovement(new Vec3(movement.x, 0, movement.z));
            }
            if (((World) level()).surface.is(Blocks.SOUL_SAND)) {
                setDeltaMovement(getDeltaMovement().multiply(.4f, 1, .4f));
            }
        }
    }

    static void field(Object target, Class<?> type, String name, Object value) throws Exception {
        var field = type.getDeclaredField(name);
        field.setAccessible(true);
        field.set(target, value);
    }

    static Method method(Class<?> type, String name, Class<?>... args) throws Exception {
        var method = type.getDeclaredMethod(name, args);
        method.setAccessible(true);
        return method;
    }

    static double[] vector(Vec3 v) {
        return new double[]{v.x, v.y, v.z};
    }

    static double[] corners(AABB box) {
        return new double[]{box.minX, box.minY, box.minZ, box.maxX, box.maxY, box.maxZ};
    }

    static Probe probe(Block surface) throws Exception {
        var world = (World) unsafe.allocateInstance(World.class);
        world.surface = surface.defaultBlockState();
        world.shapes = List.of();

        var p = (Probe) unsafe.allocateInstance(Probe.class);
        field(p, Entity.class, "level", world);
        field(p, Entity.class, "position", Vec3.ZERO);
        p.setDeltaMovement(Vec3.ZERO);
        p.accumulated = Vec3.ZERO;
        return p;
    }

    public static void main(String[] args) throws Exception {
        SharedConstants.tryDetectVersion();
        Bootstrap.bootStrap();

        var f = sun.misc.Unsafe.class.getDeclaredField("theUnsafe");
        f.setAccessible(true);
        unsafe = (sun.misc.Unsafe) f.get(null);

        sweep = method(Entity.class, "collideWithShapes", Vec3.class, AABB.class, List.class);
        collide = method(Entity.class, "collide", Vec3.class);
        air = method(LivingEntity.class, "travelInAir", Vec3.class);
        water = method(LivingEntity.class, "travelInWater", Vec3.class, double.class, boolean.class, double.class);
        lava = method(LivingEntity.class, "travelInLava", Vec3.class, double.class, boolean.class, double.class);
        glide = method(LivingEntity.class, "updateFallFlyingMovement", Vec3.class);

        var result = new LinkedHashMap<String, Object>();
        result.put("travel", travel());
        result.put("mouse", mouse());
        result.put("offsets", offsets());
        result.put("collisions", collisions());
        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(result));
    }

    static Block travelSurface(String scenario) {
        return switch (scenario) {
            case "ice" -> Blocks.ICE;
            case "soul_sand" -> Blocks.SOUL_SAND;
            default -> Blocks.STONE;
        };
    }

    static Map<String, Object> travel() throws Exception {
        var motions = new LinkedHashMap<String, Object>();
        var scenarios = List.of("stone", "ice", "soul_sand", "air", "water", "water_sprint", "lava", "glide", "climb");
        for (var scenario : scenarios) {
            var p = probe(travelSurface(scenario));
            p.ground = List.of("stone", "ice", "soul_sand").contains(scenario);
            p.ladder = scenario.equals("climb");
            p.sprint = scenario.equals("water_sprint");

            if (scenario.equals("glide")) {
                p.setDeltaMovement(new Vec3(.5, -.1, .5));
            }
            if (p.ground) {
                p.setDeltaMovement(new Vec3(0, -.08 * .98f, 0));
            }
            if (p.ladder) {
                p.setDeltaMovement(new Vec3(.4, -.3, .4));
            }

            var rows = new ArrayList<Object>();
            for (int i = 0; i < 5; i++) {
                travelTick(p, scenario);
                rows.add(Map.of("position", vector(p.accumulated), "velocity", vector(p.getDeltaMovement())));
            }
            motions.put(scenario, rows);
        }
        return motions;
    }

    static void travelTick(Probe p, String scenario) throws Exception {
        if (scenario.startsWith("water")) {
            water.invoke(p, new Vec3(0, 0, .98f), .08, p.getDeltaMovement().y <= 0, 0);
        } else if (scenario.equals("lava")) {
            lava.invoke(p, new Vec3(0, 0, .98f), .08, p.getDeltaMovement().y <= 0, 0);
        } else if (scenario.equals("glide")) {
            p.setDeltaMovement((Vec3) glide.invoke(p, p.getDeltaMovement()));
            p.accumulated = p.accumulated.add(p.getDeltaMovement());
        } else {
            air.invoke(p, new Vec3(0, 0, .98f));
        }
    }

    static List<double[]> mouse() {
        var mouse = new SmoothDouble();
        var mouseRows = new ArrayList<double[]>();
        for (var delta : new double[]{10, 0, 0, -3, 0, 0}) {
            mouseRows.add(new double[]{delta, mouse.getNewDeltaValue(delta, 1.0 / 60)});
        }
        return mouseRows;
    }

    static List<Object> offsets() {
        var offsets = new ArrayList<Object>();
        for (var block : List.of(Blocks.BAMBOO, Blocks.DANDELION, Blocks.SHORT_GRASS)) {
            for (var pos : List.of(new BlockPos(13, 5, -7), new BlockPos(-987, 0, 324))) {
                var state = block.defaultBlockState();
                var offset = state.getOffset(pos).subtract(state.getOffset(BlockPos.ZERO));
                offsets.add(Map.of(
                    "block", BuiltInRegistries.BLOCK.getKey(block).toString(),
                    "x", pos.getX(),
                    "z", pos.getZ(),
                    "offset", vector(offset)));
            }
        }
        return offsets;
    }

    static List<Object> collisions() throws Exception {
        var cases = new ArrayList<Object>();
        AABB box = new AABB(.2, 1, .2, .8, 2.8, .8);
        for (var scenario : List.of("slab", "stair", "fence", "corner_x", "corner_z", "ceiling", "epsilon")) {
            List<VoxelShape> shapes = new ArrayList<>();
            shapes.add(Shapes.box(-4, 0, -4, 4, 1, 4));
            Vec3 desired = new Vec3(.8, -.0784, .25);
            switch (scenario) {
                case "slab" -> shapes.add(Shapes.box(1, 1, 0, 2, 1.5, 1));
                case "stair" -> {
                    shapes.add(Shapes.box(1, 1, 0, 2, 1.5, 1));
                    shapes.add(Shapes.box(1.5, 1.5, 0, 2, 2, 1));
                }
                case "fence" -> shapes.add(Shapes.box(1.375, 1, .375, 1.625, 2.5, .625));
                case "corner_x", "corner_z" -> {
                    shapes.add(Shapes.box(1, 1, 1, 2, 3, 2));
                    desired = scenario.equals("corner_x") ? new Vec3(.8, -.0784, .7) : new Vec3(.7, -.0784, .8);
                }
                case "ceiling" -> {
                    shapes.add(Shapes.box(1, 1, 0, 2, 1.5, 1));
                    shapes.add(Shapes.box(0, 3, 0, 2, 4, 2));
                }
                case "epsilon" -> desired = new Vec3(1e-8, -.0784, 0);
            }

            var p = probe(Blocks.STONE);
            p.ground = true;
            field(p, Entity.class, "bb", box);
            ((World) p.level()).shapes = shapes;
            var allowed = (Vec3) collide.invoke(p, desired);

            var shapeBoxes = shapes.stream()
                .flatMap(s -> s.toAabbs().stream())
                .map(PlayerPhysicsFixtures::corners)
                .toList();
            cases.add(Map.of(
                "name", scenario,
                "box", corners(box),
                "shapes", shapeBoxes,
                "motion", vector(desired),
                "allowed", vector(allowed)));
        }
        return cases;
    }
}
