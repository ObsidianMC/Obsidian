import com.google.gson.*;
import com.mojang.serialization.*;
import io.netty.buffer.*;
import java.nio.file.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.stream.*;

/**
 * Captures metadata serializers and inherited entity layouts, and exports the serializer enum during asset
 * generation.
 */
class EntityMetadataFixtures {
    static final String ACCESSOR = "net.minecraft.network.syncher.EntityDataAccessor";
    static final String SERIALIZER = "net.minecraft.network.syncher.EntityDataSerializer";
    static final String SERIALIZERS = "net.minecraft.network.syncher.EntityDataSerializers";
    static final String[] NONE = {};

    static Object access;

    // Sample values shared by several serializers.
    static Object block;
    static Object position;
    static Object component;
    static Object item;
    static List<Object> particles;

    static Object call(String owner, String name, Object target, String[] types, Object... args) {
        return Vanilla.call(Vanilla.method(owner, name, types), target, args);
    }

    static Object holder(String name) {
        var key = Vanilla.get("net.minecraft.core.registries.Registries", name);
        var registry = call("net.minecraft.core.RegistryAccess", "lookupOrThrow", access,
            new String[]{"net.minecraft.resources.ResourceKey"}, key);
        return ((Optional<?>) call("net.minecraft.core.Registry", "getAny", registry, NONE)).orElseThrow();
    }

    static Map<String, Object> entityLayouts() {
        var layouts = new TreeMap<String, Object>();
        var entityType = Vanilla.type("net.minecraft.world.entity.EntityType");
        var entity = Vanilla.type("net.minecraft.world.entity.Entity");
        var accessor = Vanilla.type(ACCESSOR);
        var registry = Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "ENTITY_TYPE");

        var classes = new HashMap<Object, Class<?>>();
        for (var field : Vanilla.type("net.minecraft.world.entity.EntityTypes").getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) && field.getType() == entityType
                && field.getGenericType() instanceof ParameterizedType generic
                && generic.getActualTypeArguments()[0] instanceof Class<?> type) {
                classes.put(Vanilla.getStatic(field), type);
            }
        }

        for (var type : (Iterable<?>) registry) {
            var fields = new TreeMap<Integer, Object>();
            var typeClass = Objects.requireNonNull(classes.get(type), "Missing entity class");
            var parent = typeClass;
            for (; parent != null && entity.isAssignableFrom(parent); parent = parent.getSuperclass()) {
                for (var field : parent.getDeclaredFields()) {
                    if (!Modifier.isStatic(field.getModifiers()) || field.getType() != accessor) continue;

                    var data = Vanilla.getStatic(field);
                    int index = (int) call(ACCESSOR, "id", data, NONE);
                    var serializer = call(ACCESSOR, "serializer", data, NONE);
                    int id = (int) call(SERIALIZERS, "getSerializedId", null, new String[]{SERIALIZER}, serializer);
                    if (id < 0) {
                        throw new IllegalStateException("Unregistered serializer for " + field);
                    }

                    var name = Vanilla.simpleName(parent) + "." + field.getName();
                    if (fields.put(index, Map.of("serializer", id, "name", name)) != null) {
                        throw new IllegalStateException("Duplicate metadata index " + index + " on " + typeClass);
                    }
                }
            }
            var key = call("net.minecraft.core.Registry", "getKey", registry, new String[]{"java.lang.Object"}, type);
            layouts.put(key.toString(), fields);
        }
        return layouts;
    }

    public static void main(String[] args) throws Exception {
        call("net.minecraft.SharedConstants", "tryDetectVersion", null, NONE);
        call("net.minecraft.server.Bootstrap", "bootStrap", null, NONE);
        var base = loadRegistries();
        access = Vanilla.create(
            Vanilla.constructor("net.minecraft.core.RegistryAccess$ImmutableRegistryAccess", "java.util.List"),
            base);
        VanillaDumper.bindComponents(access);
        var ops = (DynamicOps<Object>) call("net.minecraft.resources.RegistryOps", "create", null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"},
            JsonOps.INSTANCE, access);
        createSamples(ops);

        var output = new TreeMap<Integer, String>();
        var names = new TreeMap<Integer, String>();
        var serializerEnum = new LinkedHashMap<String, Object>();
        var combined = Unpooled.buffer();
        for (var f : Vanilla.type(SERIALIZERS).getDeclaredFields()) {
            if (!Vanilla.type(SERIALIZER).isAssignableFrom(f.getType())) continue;

            var serializer = Vanilla.getStatic(f);
            int id = (int) call(SERIALIZERS, "getSerializedId", null, new String[]{SERIALIZER}, serializer);
            String name = f.getName();
            if (names.put(id, name) != null) {
                throw new IllegalStateException("Duplicate serializer id " + id);
            }
            serializerEnum.put(name.toLowerCase(Locale.ROOT), Map.of("protocol_id", id));
            Object value = sample(name);

            var buf = Unpooled.buffer();
            var friendly = Vanilla.create(
                Vanilla.constructor(
                    "net.minecraft.network.RegistryFriendlyByteBuf",
                    "io.netty.buffer.ByteBuf",
                    "net.minecraft.core.RegistryAccess"),
                buf,
                access);
            var codec = call(SERIALIZER, "codec", serializer, NONE);
            call("net.minecraft.network.codec.StreamEncoder", "encode", codec,
                new String[]{"java.lang.Object", "java.lang.Object"}, friendly, value);
            output.put(id, ByteBufUtil.hexDump(buf));

            // Each entry of the combined metadata: index, serializer ID, value.
            combined.writeByte(id);
            combined.writeByte(id);
            combined.writeBytes(buf);
            buf.release();
        }
        combined.writeByte(255);

        var result = new LinkedHashMap<String, Object>();
        result.put("serializers", output);
        result.put("metadata", ByteBufUtil.hexDump(combined));
        combined.release();
        result.put("serializerNames", names);
        result.put("entities", entityLayouts());
        Files.writeString(Path.of(args[0]), new GsonBuilder().setPrettyPrinting().create().toJson(result));

        // Reuse the API's existing generated-enum input; keep the public C# spelling in the source generator.
        var enumPath = Path.of(args[0]).resolveSibling("enums.json");
        var enums = JsonParser.parseString(Files.readString(enumPath)).getAsJsonObject();
        enums.add("minecraft:entity_metadata_type", new Gson().toJsonTree(serializerEnum));
        Files.writeString(enumPath, new GsonBuilder().setPrettyPrinting().create().toJson(enums));
        System.out.println("Captured " + output.size() + " metadata serializers.");
    }

    /** The built-in registries with their tags bound, followed by the data pack's world registries. */
    static List<Object> loadRegistries() {
        var builtin = Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "REGISTRY");
        var base = new ArrayList<Object>();
        ((Iterable<?>) builtin).forEach(base::add);

        var packSource = call("net.minecraft.server.packs.repository.ServerPacksSource", "createVanillaPackSource",
            null, NONE);
        var pack = call("net.minecraft.server.packs.VanillaPackResources", "fullResources", packSource, NONE);
        var resourceManager = Vanilla.constructor(
            "net.minecraft.server.packs.resources.MultiPackResourceManager",
            "net.minecraft.server.packs.PackType",
            "java.util.List");
        var resources = Vanilla.create(
            resourceManager,
            Vanilla.get("net.minecraft.server.packs.PackType", "SERVER_DATA"),
            List.of(pack));

        var tagTypes = new String[]{
            "net.minecraft.server.packs.resources.ResourceManager",
            "net.minecraft.core.Registry"};
        for (var reg : base) {
            var pending = (Optional<?>) call("net.minecraft.tags.TagLoader", "loadPendingTags", null, tagTypes,
                resources, reg);
            if (pending.isPresent()) {
                call("net.minecraft.core.Registry$PendingTags", "apply", pending.get(), NONE);
            }
        }

        var loadTypes = new String[]{
            "net.minecraft.server.packs.resources.ResourceManager",
            "java.util.List",
            "java.util.List",
            "java.util.concurrent.Executor"};
        var loading = (java.util.concurrent.CompletableFuture<?>) call(
            "net.minecraft.resources.RegistryDataLoader",
            "load",
            null,
            loadTypes,
            resources,
            base,
            Vanilla.get("net.minecraft.resources.RegistryDataLoader", "WORLD_REGISTRIES"),
            (java.util.concurrent.Executor) Runnable::run);
        var dynamic = loading.join();
        var entries = (Stream<?>) call("net.minecraft.core.RegistryAccess", "registries", dynamic, NONE);
        var entryType = "net.minecraft.core.RegistryAccess$RegistryEntry";
        entries.forEach(entry -> base.add(call(entryType, "value", entry, NONE)));
        return base;
    }

    static void createSamples(DynamicOps<Object> ops) {
        var stone = Vanilla.get("net.minecraft.world.level.block.Blocks", "STONE");
        block = call("net.minecraft.world.level.block.Block", "defaultBlockState", stone, NONE);
        position = Vanilla.create(Vanilla.constructor("net.minecraft.core.BlockPos", "int", "int", "int"), -4, 72, 9);
        component = call("net.minecraft.network.chat.Component", "literal", null,
            new String[]{"java.lang.String"}, "Fixture");

        var itemCodec = (Codec<Object>) Vanilla.get("net.minecraft.world.item.ItemStack", "CODEC");
        item = itemCodec.parse(ops, JsonParser.parseString("{\"id\":\"minecraft:diamond\",\"count\":3}")).getOrThrow();

        var particleCodec = (Codec<Object>) Vanilla.get("net.minecraft.core.particles.ParticleTypes", "CODEC");
        particles = new ArrayList<Object>();
        var inputs = List.of(
            "{\"type\":\"minecraft:block\",\"block_state\":\"minecraft:stone\"}",
            "{\"type\":\"minecraft:dust\",\"color\":16711680,\"scale\":1.5}",
            "{\"type\":\"minecraft:dust_color_transition\",\"from_color\":16711680,\"to_color\":255,\"scale\":0.5}",
            "{\"type\":\"minecraft:effect\",\"color\":-1,\"power\":0.75}",
            "{\"type\":\"minecraft:entity_effect\",\"color\":-16711936}",
            "{\"type\":\"minecraft:item\",\"item\":{\"id\":\"minecraft:diamond\"}}",
            "{\"type\":\"minecraft:sculk_charge\",\"roll\":0.25}",
            "{\"type\":\"minecraft:shriek\",\"delay\":17}",
            "{\"type\":\"minecraft:trail\",\"target\":[1,2,3],\"color\":255,\"duration\":20}",
            "{\"type\":\"minecraft:dragon_breath\",\"power\":0.5}");
        for (var input : inputs) {
            particles.add(particleCodec.parse(ops, JsonParser.parseString(input)).getOrThrow());
        }
    }

    /** The sample value written with the serializer of the name. */
    static Object sample(String name) {
        return switch (name) {
            case "BYTE" -> (byte) 33;
            case "INT" -> 300;
            case "LONG" -> -1L;
            case "FLOAT" -> 1.25f;
            case "STRING" -> "hello";
            case "COMPONENT" -> component;
            case "OPTIONAL_COMPONENT" -> Optional.of(component);
            case "ITEM_STACK" -> item;
            case "BOOLEAN" -> true;
            case "ROTATIONS" -> {
                var rotations = Vanilla.constructor("net.minecraft.core.Rotations", "float", "float", "float");
                yield Vanilla.create(rotations, 10f, 20f, 30f);
            }
            case "BLOCK_POS" -> position;
            case "OPTIONAL_BLOCK_POS" -> Optional.of(position);
            case "DIRECTION" -> Vanilla.get("net.minecraft.core.Direction", "EAST");
            case "OPTIONAL_LIVING_ENTITY_REFERENCE" -> {
                var uuid = UUID.fromString("00112233-4455-6677-8899-aabbccddeeff");
                yield Optional.of(call("net.minecraft.world.entity.EntityReference", "of", null,
                    new String[]{"java.util.UUID"}, uuid));
            }
            case "BLOCK_STATE" -> block;
            case "OPTIONAL_BLOCK_STATE" -> Optional.of(block);
            case "PARTICLE" -> particles.get(1);
            case "PARTICLES" -> particles;
            case "VILLAGER_DATA" -> {
                var villagerData = Vanilla.constructor(
                    "net.minecraft.world.entity.npc.villager.VillagerData",
                    "net.minecraft.core.Holder",
                    "net.minecraft.core.Holder",
                    "int");
                yield Vanilla.create(villagerData, holder("VILLAGER_TYPE"), holder("VILLAGER_PROFESSION"), 3);
            }
            case "OPTIONAL_UNSIGNED_INT" -> OptionalInt.of(42);
            case "POSE" -> Vanilla.get("net.minecraft.world.entity.Pose", "SWIMMING");
            case "CAT_VARIANT", "CAT_SOUND_VARIANT",
                "COW_VARIANT", "COW_SOUND_VARIANT",
                "WOLF_VARIANT", "WOLF_SOUND_VARIANT",
                "FROG_VARIANT",
                "PIG_VARIANT", "PIG_SOUND_VARIANT",
                "CHICKEN_VARIANT", "CHICKEN_SOUND_VARIANT",
                "ZOMBIE_NAUTILUS_VARIANT",
                "PAINTING_VARIANT" -> holder(name);
            case "OPTIONAL_GLOBAL_POS" -> {
                var overworld = Vanilla.get("net.minecraft.world.level.Level", "OVERWORLD");
                yield Optional.of(call("net.minecraft.core.GlobalPos", "of", null,
                    new String[]{"net.minecraft.resources.ResourceKey", "net.minecraft.core.BlockPos"},
                    overworld, position));
            }
            case "SNIFFER_STATE" -> Vanilla.get("net.minecraft.world.entity.animal.sniffer.Sniffer$State", "DIGGING");
            case "ARMADILLO_STATE" ->
                Vanilla.get("net.minecraft.world.entity.animal.armadillo.Armadillo$ArmadilloState", "SCARED");
            case "COPPER_GOLEM_STATE" ->
                Vanilla.get("net.minecraft.world.entity.animal.golem.CopperGolemState", "IDLE");
            case "WEATHERING_COPPER_STATE" ->
                Vanilla.get("net.minecraft.world.level.block.WeatheringCopper$WeatherState", "OXIDIZED");
            case "VECTOR3" -> new org.joml.Vector3f(1, 2, 3);
            case "QUATERNION" -> new org.joml.Quaternionf(0, 0, 0, 1);
            case "RESOLVABLE_PROFILE" -> call("net.minecraft.world.item.component.ResolvableProfile",
                "createUnresolved", null, new String[]{"java.lang.String"}, "Fixture");
            case "HUMANOID_ARM" -> Vanilla.get("net.minecraft.world.entity.HumanoidArm", "LEFT");
            case "DYE_COLOR" -> Vanilla.get("net.minecraft.world.item.DyeColor", "BLUE");
            default -> throw new IllegalStateException(name);
        };
    }
}
