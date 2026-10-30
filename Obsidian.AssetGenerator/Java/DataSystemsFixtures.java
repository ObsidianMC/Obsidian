import com.google.gson.*;
import com.mojang.serialization.JsonOps;
import java.lang.reflect.Proxy;
import java.nio.file.*;
import java.util.*;
import net.minecraft.SharedConstants;
import net.minecraft.core.*;
import net.minecraft.core.component.*;
import net.minecraft.core.registries.*;
import net.minecraft.server.Bootstrap;
import net.minecraft.util.RandomSource;
import net.minecraft.world.clock.*;
import net.minecraft.world.level.*;
import net.minecraft.world.level.block.*;
import net.minecraft.world.level.block.state.BlockState;
import net.minecraft.world.level.block.state.properties.BlockStateProperties;
import net.minecraft.world.item.equipment.trim.TrimMaterial;
import net.minecraft.world.attribute.EnvironmentAttribute;
import net.minecraft.world.attribute.EnvironmentAttributes;
import net.minecraft.world.phys.Vec3;

/**
 * Captures transformer providers, mushroom survival, clocks/markers and item-supplied pottery/trim data.
 * Usage: java DataSystemsFixtures.java <data-systems-26.3.json>, with the server class path.
 * Terrain proxies supply only terrain/light queries; vanilla evaluates every rule.
 */
class DataSystemsFixtures {
    static BlockState ground;
    static BlockState above;
    static int brightness;
    static RegistryAccess access;
    static final RandomSource random = RandomSource.create(0);

    static BlockState blockAt(BlockPos position) {
        return position.getY() == 1 ? above : ground;
    }

    static Object terrain(Class<?> type) {
        return Proxy.newProxyInstance(type.getClassLoader(), new Class<?>[] { type }, (proxy, method, args) -> {
            return switch (method.getName()) {
                case "getBlockState" -> blockAt((BlockPos)args[0]);
                case "getFluidState" -> blockAt((BlockPos)args[0]).getFluidState();
                case "getRawBrightness" -> brightness;
                case "getRandom" -> random;
                case "registryAccess" -> access;
                default -> throw new UnsupportedOperationException(method.toString());
            };
        });
    }

    static String name(BlockState state) {
        return BuiltInRegistries.BLOCK.getKey(state.getBlock()).toString();
    }

    static JsonObject state(BlockState state) {
        var properties = new JsonObject();
        state.getValues().forEach(property -> {
            properties.addProperty(property.property().getName(), property.valueName());
        });

        var value = new JsonObject();
        value.addProperty("id", name(state));
        value.add("properties", properties);
        return value;
    }

    static Set<BlockState> inputs(Block block) {
        var inputs = new LinkedHashSet<BlockState>();
        var defaultState = block.defaultBlockState();
        inputs.add(defaultState);
        if (defaultState.hasProperty(BlockStateProperties.AXIS)) {
            for (var axis : Direction.Axis.values()) {
                inputs.add(defaultState.setValue(BlockStateProperties.AXIS, axis));
            }
        }

        // A non-default state exercises property retention on copper stairs, doors, chests and slabs.
        inputs.add(block.getStateDefinition().getPossibleStates().getLast());
        return inputs;
    }

    static boolean eligible(BlockTransformer transformer, LevelAccessor level) {
        for (var transform : transformer.transforms()) {
            var provider = transform.blockStateProvider().value();
            if (provider.getOptionalState(level, random, BlockPos.ZERO) != null) {
                return true;
            }
        }
        return false;
    }

    static JsonObject transformation(String id, BlockTransformer transformer, LevelAccessor level, Direction face) {
        var sample = new JsonObject();
        sample.addProperty("transformer", id);
        sample.add("input", state(ground));
        sample.addProperty("above", name(above));
        sample.addProperty("face", face.getSerializedName());

        for (var transform : transformer.transforms()) {
            if (transform.disallowedFaces().contains(face)) {
                continue;
            }

            var provider = transform.blockStateProvider().value();
            var output = provider.getOptionalState(level, random, BlockPos.ZERO);
            if (output == null) {
                continue;
            }

            sample.add("output", state(output));
            sample.addProperty("sound", transform.sound().value().location().toString());
            sample.addProperty("damage", transform.itemDamagePerUse());
            transform.loot().ifPresent(key -> {
                sample.addProperty("loot", key.identifier().toString());
            });
            break;
        }
        return sample;
    }

    static JsonArray transformers() {
        var samples = new JsonArray();
        var level = (LevelAccessor)terrain(LevelAccessor.class);
        var registry = access.lookupOrThrow(Registries.BLOCK_TRANSFORMER);

        for (var entry : registry.entrySet()) {
            var transformer = entry.getValue();
            var id = entry.getKey().identifier().toString();
            for (var block : BuiltInRegistries.BLOCK) {
                for (var input : inputs(block)) {
                    ground = input;
                    above = Blocks.AIR.defaultBlockState();
                    boolean includeFailure = block == Blocks.STONE
                        || block == Blocks.DIRT
                        || block == Blocks.ROOTED_DIRT
                        || block instanceof CopperChestBlock;
                    if (!eligible(transformer, level) && !includeFailure) {
                        continue;
                    }

                    for (var cover : List.of(Blocks.AIR, Blocks.STONE, Blocks.CAVE_AIR)) {
                        above = cover.defaultBlockState();
                        for (var face : Direction.values()) {
                            samples.add(transformation(id, transformer, level, face));
                        }
                    }
                }
            }
        }
        return samples;
    }

    static JsonArray mushrooms() {
        var samples = new JsonArray();
        var reader = (LevelReader)terrain(LevelReader.class);
        var mushroom = Blocks.BROWN_MUSHROOM.defaultBlockState();
        var position = BlockPos.ZERO.above();

        for (var block : BuiltInRegistries.BLOCK) {
            ground = block.defaultBlockState();
            for (var light : new int[] { 0, 12, 13, 15 }) {
                brightness = light;
                boolean survives = mushroom.canSurvive(reader, position);

                var sample = new JsonObject();
                sample.addProperty("below", name(ground));
                sample.addProperty("light", light);
                sample.addProperty("survives", survives);
                samples.add(sample);
            }
        }
        return samples;
    }

    static JsonElement clockState(ClockState state) {
        return ClockState.CODEC.encodeStart(JsonOps.INSTANCE, state).getOrThrow();
    }

    static JsonArray clocks() {
        var samples = new JsonArray();
        for (var rate : new float[] { 0.25f, 0.5f, 1, 2.5f, 20 }) {
            for (var paused : new boolean[] { false, true }) {
                var initial = new ClockState(23999L, 0.75f, rate, paused);
                var clock = new ServerClockManager.ServerClockInstance();
                clock.loadFrom(initial);

                var ticks = new JsonArray();
                for (int n = 0; n < 5; n++) {
                    clock.tick();
                    ticks.add(clockState(clock.packState()));
                }

                var sample = new JsonObject();
                sample.add("initial", clockState(initial));
                sample.add("ticks", ticks);
                samples.add(sample);
            }
        }
        return samples;
    }

    static JsonArray markers() {
        var samples = new JsonArray();
        long[] times = { 0, 999, 1000, 6000, 13000, 18000, 23999, 24000, 25000, 999999 };
        for (var timeline : access.lookupOrThrow(Registries.TIMELINE)) {
            timeline.registerTimeMarkers((key, marker) -> {
                for (var time : times) {
                    var sample = new JsonObject();
                    sample.addProperty("name", key.identifier().toString());
                    sample.addProperty("time", time);
                    sample.addProperty("next", marker.resolveTimeToMoveTo(time));
                    sample.addProperty("at", marker.occursAt(time));
                    samples.add(sample);
                }
            });
        }
        return samples;
    }

    static JsonArray pottery() {
        var samples = new JsonArray();
        for (var item : BuiltInRegistries.ITEM) {
            var pattern = item.components().get(DataComponents.PROVIDES_POTTERY_PATTERN);
            if (pattern == null) {
                continue;
            }

            var sample = new JsonObject();
            sample.addProperty("item", BuiltInRegistries.ITEM.getKey(item).toString());
            sample.addProperty("pattern", pattern.unwrapKey().orElseThrow().identifier().toString());
            sample.addProperty("asset", pattern.value().assetId().toString());
            samples.add(sample);
        }
        return samples;
    }

    static <T> void track(
        JsonArray samples,
        String name,
        EnvironmentAttribute<T> attribute,
        T initial
    ) {
        var clock = new ServerClockManager.ServerClockInstance();
        ClockManager manager = holder -> clock;
        var day = access.lookupOrThrow(Registries.TIMELINE).getValue(net.minecraft.resources.Identifier.parse(name));
        var sampler = day.createTrackSampler(attribute, manager);
        int tickId = 0;
        long[] times = { 0, 132, 133, 1000, 11867, 12541, 12542, 13670, 18000, 22330, 23459, 23460,
            23999, 24000, 119999, 120000 };

        for (var time : times) {
            clock.loadFrom(new ClockState(time, 0, 1, false));
            var value = sampler.applyTimeBased(initial, ++tickId);

            var sample = new JsonObject();
            sample.addProperty("timeline", name);
            sample.addProperty("attribute", BuiltInRegistries.ENVIRONMENT_ATTRIBUTE.getKey(attribute).toString());
            sample.addProperty("time", time);
            sample.add("initial", new Gson().toJsonTree(initial));
            sample.add("value", new Gson().toJsonTree(value));
            samples.add(sample);
        }
    }

    static JsonArray tracks() {
        var samples = new JsonArray();
        track(samples, "minecraft:day", EnvironmentAttributes.SKY_LIGHT_LEVEL, 15f);
        track(samples, "minecraft:day", EnvironmentAttributes.MONSTERS_BURN, false);
        track(samples, "minecraft:early_game", EnvironmentAttributes.CAN_PILLAGER_PATROL_SPAWN, true);
        return samples;
    }

    static JsonObject trims() {
        var samples = new JsonObject();
        for (var entry : access.lookupOrThrow(Registries.TRIM_MATERIAL).entrySet()) {
            var encoded = TrimMaterial.DIRECT_CODEC.encodeStart(JsonOps.INSTANCE, entry.getValue()).getOrThrow();
            samples.add(entry.getKey().identifier().toString(), encoded);
        }
        return samples;
    }

    static JsonArray chestUses() {
        var samples = new JsonArray();
        for (var block : BuiltInRegistries.BLOCK) {
            if (!(block instanceof CopperChestBlock)) {
                continue;
            }

            var state = block.defaultBlockState();
            var hit = new net.minecraft.world.phys.BlockHitResult(Vec3.ZERO, Direction.UP, BlockPos.ZERO, false);
            var result = state.useWithoutItem(null, null, hit);
            var sample = new JsonObject();
            sample.add("input", state(state));
            sample.addProperty("consumes", result.consumesAction());
            samples.add(sample);
        }
        return samples;
    }

    public static void main(String[] args) throws Exception {
        SharedConstants.tryDetectVersion();
        Bootstrap.bootStrap();
        access = JoinFixtures.loadRegistries();
        VanillaDumper.bindComponents(access);

        var result = new JsonObject();
        result.add("transformers", transformers());
        result.add("mushrooms", mushrooms());
        result.add("clocks", clocks());
        result.add("markers", markers());
        result.add("tracks", tracks());
        result.add("pottery", pottery());
        result.add("trims", trims());
        result.add("chest_uses", chestUses());

        Files.writeString(Path.of(args[0]), new Gson().toJson(result));
    }
}
