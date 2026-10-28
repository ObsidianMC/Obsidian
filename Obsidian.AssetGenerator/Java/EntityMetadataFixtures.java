import com.google.gson.*;
import com.mojang.serialization.*;
import io.netty.buffer.*;
import java.nio.file.*;
import java.lang.reflect.*;
import java.util.*;
import java.util.stream.*;

/** Captures metadata serializers and inherited entity layouts, and exports the serializer enum during asset generation. */
class EntityMetadataFixtures {
    static Object access;
    static Object call(String owner, String name, Object target, String[] types, Object... args) {
        return Vanilla.call(Vanilla.method(owner, name, types), target, args);
    }
    static Object holder(String name) {
        var registry = call("net.minecraft.core.RegistryAccess", "lookupOrThrow", access,
            new String[]{"net.minecraft.resources.ResourceKey"}, Vanilla.get("net.minecraft.core.registries.Registries", name));
        return ((Optional<?>)call("net.minecraft.core.Registry", "getAny", registry, new String[]{})).orElseThrow();
    }
    static Map<String, Object> entityLayouts() {
        var layouts = new TreeMap<String, Object>();
        var entityType = Vanilla.type("net.minecraft.world.entity.EntityType");
        var entity = Vanilla.type("net.minecraft.world.entity.Entity");
        var accessor = Vanilla.type("net.minecraft.network.syncher.EntityDataAccessor");
        var registry = Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "ENTITY_TYPE");
        var classes = new HashMap<Object, Class<?>>();
        for (var field : Vanilla.type("net.minecraft.world.entity.EntityTypes").getDeclaredFields()) {
            if (Modifier.isStatic(field.getModifiers()) && field.getType() == entityType
                && field.getGenericType() instanceof ParameterizedType generic
                && generic.getActualTypeArguments()[0] instanceof Class<?> type)
                classes.put(Vanilla.getStatic(field), type);
        }
        for (var type : (Iterable<?>)registry) {
            var fields = new TreeMap<Integer, Object>();
            var typeClass = Objects.requireNonNull(classes.get(type), "Missing entity class");
            for (var parent = typeClass; parent != null && entity.isAssignableFrom(parent); parent = parent.getSuperclass()) {
                for (var field : parent.getDeclaredFields()) {
                    if (!Modifier.isStatic(field.getModifiers()) || field.getType() != accessor) continue;
                    var data = Vanilla.getStatic(field);
                    int index = (int)call("net.minecraft.network.syncher.EntityDataAccessor", "id", data, new String[]{});
                    var serializer = call("net.minecraft.network.syncher.EntityDataAccessor", "serializer", data, new String[]{});
                    int id = (int)call("net.minecraft.network.syncher.EntityDataSerializers", "getSerializedId", null,
                        new String[]{"net.minecraft.network.syncher.EntityDataSerializer"}, serializer);
                    if (id < 0) throw new IllegalStateException("Unregistered serializer for " + field);
                    if (fields.put(index, Map.of("serializer", id, "name", Vanilla.simpleName(parent) + "." + field.getName())) != null)
                        throw new IllegalStateException("Duplicate metadata index " + index + " on " + typeClass);
                }
            }
            String key = call("net.minecraft.core.Registry", "getKey", registry, new String[]{"java.lang.Object"}, type).toString();
            layouts.put(key, fields);
        }
        return layouts;
    }
    public static void main(String[] args) throws Exception {
        call("net.minecraft.SharedConstants", "tryDetectVersion", null, new String[]{});
        call("net.minecraft.server.Bootstrap", "bootStrap", null, new String[]{});
        var builtin = Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "REGISTRY");
        var base = new ArrayList<Object>();
        ((Iterable<?>)builtin).forEach(base::add);
        var pack = call("net.minecraft.server.packs.VanillaPackResources", "fullResources", call("net.minecraft.server.packs.repository.ServerPacksSource", "createVanillaPackSource", null, new String[]{}), new String[]{});
        var resources = Vanilla.create(Vanilla.constructor("net.minecraft.server.packs.resources.MultiPackResourceManager", "net.minecraft.server.packs.PackType", "java.util.List"), Vanilla.get("net.minecraft.server.packs.PackType", "SERVER_DATA"), List.of(pack));
        for (var reg : base) {
            var pending = (Optional<?>)call("net.minecraft.tags.TagLoader", "loadPendingTags", null,
                new String[]{"net.minecraft.server.packs.resources.ResourceManager", "net.minecraft.core.Registry"}, resources, reg);
            if (pending.isPresent()) call("net.minecraft.core.Registry$PendingTags", "apply", pending.get(), new String[]{});
        }
        var dynamic = ((java.util.concurrent.CompletableFuture<?>) call("net.minecraft.resources.RegistryDataLoader", "load", null,
            new String[]{"net.minecraft.server.packs.resources.ResourceManager", "java.util.List", "java.util.List", "java.util.concurrent.Executor"}, resources, base,
            Vanilla.get("net.minecraft.resources.RegistryDataLoader", "WORLD_REGISTRIES"), (java.util.concurrent.Executor) Runnable::run)).join();
        var entries = (Stream<?>)call("net.minecraft.core.RegistryAccess", "registries", dynamic, new String[]{});
        entries.forEach(entry -> base.add(call("net.minecraft.core.RegistryAccess$RegistryEntry", "value", entry, new String[]{})));
        access = Vanilla.create(Vanilla.constructor("net.minecraft.core.RegistryAccess$ImmutableRegistryAccess", "java.util.List"), base);
        VanillaDumper.bindComponents(access);
        var ops = (DynamicOps<Object>)call("net.minecraft.resources.RegistryOps", "create", null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"}, JsonOps.INSTANCE, access);

        var serializers = "net.minecraft.network.syncher.EntityDataSerializers";
        var block = call("net.minecraft.world.level.block.Block", "defaultBlockState", Vanilla.get("net.minecraft.world.level.block.Blocks", "STONE"), new String[]{});
        var position = Vanilla.create(Vanilla.constructor("net.minecraft.core.BlockPos", "int", "int", "int"), -4, 72, 9);
        var component = call("net.minecraft.network.chat.Component", "literal", null, new String[]{"java.lang.String"}, "Fixture");
        var itemCodec = (Codec<Object>)Vanilla.get("net.minecraft.world.item.ItemStack", "CODEC");
        var item = itemCodec.parse(ops, JsonParser.parseString("{\"id\":\"minecraft:diamond\",\"count\":3}")).getOrThrow();
        var particleCodec = (Codec<Object>)Vanilla.get("net.minecraft.core.particles.ParticleTypes", "CODEC");
        var particles = new ArrayList<Object>();
        for (var input : List.of(
            "{\"type\":\"minecraft:block\",\"block_state\":\"minecraft:stone\"}",
            "{\"type\":\"minecraft:dust\",\"color\":16711680,\"scale\":1.5}",
            "{\"type\":\"minecraft:dust_color_transition\",\"from_color\":16711680,\"to_color\":255,\"scale\":0.5}",
            "{\"type\":\"minecraft:effect\",\"color\":-1,\"power\":0.75}",
            "{\"type\":\"minecraft:entity_effect\",\"color\":-16711936}",
            "{\"type\":\"minecraft:item\",\"item\":{\"id\":\"minecraft:diamond\"}}",
            "{\"type\":\"minecraft:sculk_charge\",\"roll\":0.25}",
            "{\"type\":\"minecraft:shriek\",\"delay\":17}",
            "{\"type\":\"minecraft:trail\",\"target\":[1,2,3],\"color\":255,\"duration\":20}",
            "{\"type\":\"minecraft:dragon_breath\",\"power\":0.5}"))
            particles.add(particleCodec.parse(ops, JsonParser.parseString(input)).getOrThrow());
        var output = new TreeMap<Integer, String>();
        var names = new TreeMap<Integer, String>();
        var serializerEnum = new LinkedHashMap<String, Object>();
        var combined = Unpooled.buffer();
        for (var f : Vanilla.type(serializers).getDeclaredFields()) {
            if (!Vanilla.type("net.minecraft.network.syncher.EntityDataSerializer").isAssignableFrom(f.getType())) continue;
            var serializer = Vanilla.getStatic(f);
            int id = (int)call(serializers, "getSerializedId", null, new String[]{"net.minecraft.network.syncher.EntityDataSerializer"}, serializer);
            String name = f.getName();
            if (names.put(id, name) != null) throw new IllegalStateException("Duplicate serializer id " + id);
            serializerEnum.put(name.toLowerCase(Locale.ROOT), Map.of("protocol_id", id));
            Object value = switch(name) {
                case "BYTE" -> (byte)33;
                case "INT" -> 300;
                case "LONG" -> -1L;
                case "FLOAT" -> 1.25f;
                case "STRING" -> "hello";
                case "COMPONENT" -> component;
                case "OPTIONAL_COMPONENT" -> Optional.of(component);
                case "ITEM_STACK" -> item;
                case "BOOLEAN" -> true;
                case "ROTATIONS" -> Vanilla.create(Vanilla.constructor("net.minecraft.core.Rotations", "float", "float", "float"), 10f, 20f, 30f);
                case "BLOCK_POS" -> position;
                case "OPTIONAL_BLOCK_POS" -> Optional.of(position);
                case "DIRECTION" -> Vanilla.get("net.minecraft.core.Direction", "EAST");
                case "OPTIONAL_LIVING_ENTITY_REFERENCE" -> Optional.of(call("net.minecraft.world.entity.EntityReference", "of", null, new String[]{"java.util.UUID"}, UUID.fromString("00112233-4455-6677-8899-aabbccddeeff")));
                case "BLOCK_STATE" -> block;
                case "OPTIONAL_BLOCK_STATE" -> Optional.of(block);
                case "PARTICLE" -> particles.get(1);
                case "PARTICLES" -> particles;
                case "VILLAGER_DATA" -> Vanilla.create(Vanilla.constructor("net.minecraft.world.entity.npc.villager.VillagerData", "net.minecraft.core.Holder", "net.minecraft.core.Holder", "int"), holder("VILLAGER_TYPE"), holder("VILLAGER_PROFESSION"), 3);
                case "OPTIONAL_UNSIGNED_INT" -> OptionalInt.of(42);
                case "POSE" -> Vanilla.get("net.minecraft.world.entity.Pose", "SWIMMING");
                case "CAT_VARIANT", "CAT_SOUND_VARIANT", "COW_VARIANT", "COW_SOUND_VARIANT", "WOLF_VARIANT", "WOLF_SOUND_VARIANT", "FROG_VARIANT",
                    "PIG_VARIANT", "PIG_SOUND_VARIANT", "CHICKEN_VARIANT", "CHICKEN_SOUND_VARIANT", "ZOMBIE_NAUTILUS_VARIANT", "PAINTING_VARIANT" -> holder(name);
                case "OPTIONAL_GLOBAL_POS" -> Optional.of(call("net.minecraft.core.GlobalPos", "of", null, new String[]{"net.minecraft.resources.ResourceKey", "net.minecraft.core.BlockPos"}, Vanilla.get("net.minecraft.world.level.Level", "OVERWORLD"), position));
                case "SNIFFER_STATE" -> Vanilla.get("net.minecraft.world.entity.animal.sniffer.Sniffer$State", "DIGGING");
                case "ARMADILLO_STATE" -> Vanilla.get("net.minecraft.world.entity.animal.armadillo.Armadillo$ArmadilloState", "SCARED");
                case "COPPER_GOLEM_STATE" -> Vanilla.get("net.minecraft.world.entity.animal.golem.CopperGolemState", "IDLE");
                case "WEATHERING_COPPER_STATE" -> Vanilla.get("net.minecraft.world.level.block.WeatheringCopper$WeatherState", "OXIDIZED");
                case "VECTOR3" -> new org.joml.Vector3f(1,2,3);
                case "QUATERNION" -> new org.joml.Quaternionf(0,0,0,1);
                case "RESOLVABLE_PROFILE" -> call("net.minecraft.world.item.component.ResolvableProfile", "createUnresolved", null, new String[]{"java.lang.String"}, "Fixture");
                case "HUMANOID_ARM" -> Vanilla.get("net.minecraft.world.entity.HumanoidArm", "LEFT");
                case "DYE_COLOR" -> Vanilla.get("net.minecraft.world.item.DyeColor", "BLUE");
                default -> throw new IllegalStateException(name);
            };
            var buf = Unpooled.buffer();
            var friendly = Vanilla.create(Vanilla.constructor("net.minecraft.network.RegistryFriendlyByteBuf", "io.netty.buffer.ByteBuf", "net.minecraft.core.RegistryAccess"), buf, access);
            var codec = call("net.minecraft.network.syncher.EntityDataSerializer", "codec", serializer, new String[]{});
            call("net.minecraft.network.codec.StreamEncoder", "encode", codec, new String[]{"java.lang.Object", "java.lang.Object"}, friendly, value);
            output.put(id, ByteBufUtil.hexDump(buf));
            combined.writeByte(id); combined.writeByte(id); combined.writeBytes(buf); buf.release();
        }
        combined.writeByte(255);
        var result = new LinkedHashMap<String,Object>(); result.put("serializers", output); result.put("metadata",ByteBufUtil.hexDump(combined)); combined.release();
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
}
