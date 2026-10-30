import com.google.gson.*;
import com.mojang.serialization.*;
import com.google.common.hash.HashCode;
import io.netty.buffer.*;
import java.nio.file.*;
import java.util.*;
import java.util.stream.*;

/** Captures wire bytes and HashOps results by running the actual vanilla codecs. No server is started. */
public class ComponentFixtures {
    static final String REGISTRY = "net.minecraft.core.Registry";
    static final String TYPED_COMPONENT = "net.minecraft.core.component.TypedDataComponent";
    static final String COMPONENT_TYPE = "net.minecraft.core.component.DataComponentType";
    static final String[] OBJECT = {"java.lang.Object"};
    static final String[] NONE = {};

    static Object access;
    static DynamicOps<Object> ops;
    static DynamicOps<Object> hashOps;
    static java.lang.reflect.Method encode;
    static Object componentRegistry;

    static Object call(String owner, String name, Object target, String[] types, Object... args) {
        return Vanilla.call(Vanilla.method(owner, name, types), target, args);
    }

    static Object friendly(ByteBuf buffer) {
        var constructor = Vanilla.constructor(
            "net.minecraft.network.RegistryFriendlyByteBuf",
            "io.netty.buffer.ByteBuf",
            "net.minecraft.core.RegistryAccess");
        return Vanilla.create(constructor, buffer, access);
    }

    static void writeVarInt(ByteBuf buffer, int value) {
        var types = new String[]{"io.netty.buffer.ByteBuf", "int"};
        call("net.minecraft.network.VarInt", "write", null, types, buffer, value);
    }

    static String key(Object registry, Object value) {
        return call(REGISTRY, "getKey", registry, OBJECT, value).toString();
    }

    static int hash(Codec<Object> codec, Object value) {
        return ((HashCode) codec.encodeStart(hashOps, value).getOrThrow()).asInt();
    }

    static JsonElement readJson(Path path) throws Exception {
        return JsonParser.parseString(Files.readString(path));
    }

    public static void main(String[] args) throws Exception {
        call("net.minecraft.SharedConstants", "tryDetectVersion", null, NONE);
        call("net.minecraft.server.Bootstrap", "bootStrap", null, NONE);
        var base = loadRegistries();
        access = Vanilla.create(
            Vanilla.constructor("net.minecraft.core.RegistryAccess$ImmutableRegistryAccess", "java.util.List"),
            base);
        VanillaDumper.bindComponents(access);

        var opsTypes = new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"};
        var crc32c = Vanilla.get("net.minecraft.util.HashOps", "CRC32C_INSTANCE");
        var registryOps = "net.minecraft.resources.RegistryOps";
        ops = (DynamicOps<Object>) call(registryOps, "create", null, opsTypes, JsonOps.INSTANCE, access);
        hashOps = (DynamicOps<Object>) call(registryOps, "create", null, opsTypes, crc32c, access);
        encode = Vanilla.method(
            "net.minecraft.network.codec.StreamEncoder",
            "encode",
            "java.lang.Object",
            "java.lang.Object");
        componentRegistry = Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "DATA_COMPONENT_TYPE");

        var output = new JsonObject();
        var registry = new JsonArray();
        for (var type : (Iterable<?>) componentRegistry) {
            registry.add(key(componentRegistry, type));
        }
        output.add("componentTypes", registry);

        var registryNames = new JsonObject();
        for (var reg : base) {
            var names = new JsonArray();
            for (var value : (Iterable<?>) reg) {
                names.add(key(reg, value));
            }
            var key = call(REGISTRY, "key", reg, NONE);
            registryNames.add(call("net.minecraft.resources.ResourceKey", "identifier", key, NONE).toString(), names);
        }
        output.add("registries", hashRegistries(registryNames));

        // Persistent item codecs need names for these holders even when they arrived as network IDs.
        var componentRegistries = new JsonObject();
        for (var key : List.of("minecraft:banner_pattern", "minecraft:jukebox_song", "minecraft:damage_type")) {
            componentRegistries.add(key, registryNames.get(key));
        }
        Files.writeString(Path.of(args[1]).resolveSibling("component_registry_ids.json"),
            new GsonBuilder().setPrettyPrinting().create().toJson(componentRegistries) + "\n");

        var cases = new JsonArray();
        for (var input : readJson(Path.of(args[0])).getAsJsonArray()) {
            cases.add(stackCase(input.getAsJsonObject()));
        }
        var samples = readJson(Path.of(args[0]).resolveSibling("component-samples.json")).getAsJsonObject();
        for (var type : (Iterable<?>) componentRegistry) {
            cases.add(componentCase(type, samples));
        }
        output.add("cases", cases);

        var merchantInputs = readJson(Path.of(args[0]).resolveSibling("merchant-fixtures-input.json"));
        output.add("merchantOffers", merchantOffers(merchantInputs));

        var json = new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create().toJson(output);
        Files.writeString(Path.of(args[1]), json + "\n");
        System.out.println("Captured " + cases.size() + " item stacks and " + registry.size() + " component IDs.");
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

        var tagTypes = new String[]{"net.minecraft.server.packs.resources.ResourceManager", REGISTRY};
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

    static JsonObject hashRegistries(JsonObject registryNames) {
        // Only registries used by the hash tests are included; their order is connection-specific in real clients.
        var hashRegistries = new JsonObject();
        var keys = List.of(
            "minecraft:attribute",
            "minecraft:enchantment",
            "minecraft:potion",
            "minecraft:mob_effect",
            "minecraft:banner_pattern",
            "minecraft:trim_material",
            "minecraft:trim_pattern",
            "minecraft:instrument",
            "minecraft:sound_event",
            "minecraft:entity_type",
            "minecraft:block_transformer",
            "minecraft:decorated_pot_pattern",
            "minecraft:cat_sound_variant",
            "minecraft:chicken_sound_variant",
            "minecraft:cow_sound_variant",
            "minecraft:pig_sound_variant",
            "minecraft:damage_type");
        for (var key : keys) {
            hashRegistries.add(key, registryNames.get(key));
        }

        // Tool fixture uses stone (ID 1); retaining the prefix avoids committing the entire block registry.
        var blocks = new JsonArray();
        for (int id = 0; id <= 1; id++) {
            blocks.add(registryNames.getAsJsonArray("minecraft:block").get(id));
        }
        hashRegistries.add("minecraft:block", blocks);
        return hashRegistries;
    }

    /** The wire, untrusted wire and hashed forms of an item stack fixture. */
    static JsonObject stackCase(JsonObject obj) {
        var codec = (Codec<Object>) Vanilla.get("net.minecraft.world.item.ItemStack", "CODEC");
        var stream = Vanilla.get("net.minecraft.world.item.ItemStack", "OPTIONAL_STREAM_CODEC");
        var untrusted = Vanilla.get("net.minecraft.world.item.ItemStack", "OPTIONAL_UNTRUSTED_STREAM_CODEC");
        Object stack;
        try {
            stack = codec.parse(ops, obj.get("stack")).getOrThrow();
        } catch (Exception e) {
            throw new IllegalStateException("Fixture " + obj.get("name"), e);
        }

        var row = new JsonObject();
        row.add("name", obj.get("name"));
        for (var variant : Map.of("wire", stream, "untrusted", untrusted).entrySet()) {
            var buf = Unpooled.buffer();
            Vanilla.call(encode, variant.getValue(), friendly(buf), stack);
            row.addProperty(variant.getKey(), ByteBufUtil.hexDump(buf));
            buf.release();
        }

        var generatorName = "net.minecraft.network.HashedPatchMap$HashGenerator";
        var generator = java.lang.reflect.Proxy.newProxyInstance(
            ComponentFixtures.class.getClassLoader(),
            new Class<?>[]{Vanilla.type(generatorName)},
            (proxy, method, arguments) -> {
                var opsType = new String[]{"com.mojang.serialization.DynamicOps"};
                var result = (DataResult<?>) call(TYPED_COMPONENT, "encodeValue", arguments[0], opsType, hashOps);
                return ((HashCode) result.getOrThrow()).asInt();
            });
        var hashedTypes = new String[]{"net.minecraft.world.item.ItemStack", generatorName};
        var hashed = call("net.minecraft.network.HashedStack", "create", null, hashedTypes, stack, generator);
        var hashBuffer = Unpooled.buffer();
        var hashedCodec = Vanilla.get("net.minecraft.network.HashedStack", "STREAM_CODEC");
        Vanilla.call(encode, hashedCodec, friendly(hashBuffer), hashed);
        row.addProperty("hashed", ByteBufUtil.hexDump(hashBuffer));
        hashBuffer.release();

        var patch = call("net.minecraft.world.item.ItemStack", "getComponentsPatch", stack, NONE);
        var hashes = new JsonObject();
        // The patch's added components; removed ones have no value to hash.
        var split = call("net.minecraft.core.component.DataComponentPatch", "split", patch, NONE);
        var added = call("net.minecraft.core.component.DataComponentPatch$SplitResult", "added", split, NONE);
        for (var component : (Iterable<?>) added) {
            var type = call(TYPED_COMPONENT, "type", component, NONE);
            var typeCodec = (Codec<Object>) call(COMPONENT_TYPE, "codec", type, NONE);
            if (typeCodec == null) continue;

            var id = (int) call(REGISTRY, "getId", componentRegistry, OBJECT, type);
            var value = call(TYPED_COMPONENT, "value", component, NONE);
            hashes.addProperty(Integer.toString(id), hash(typeCodec, value));
        }
        row.add("hashes", hashes);
        return row;
    }

    /** A single-component patch of the type holding its sample value, framed as the wire and untrusted codecs do. */
    static JsonObject componentCase(Object type, JsonObject samples) {
        var id = (int) call(REGISTRY, "getId", componentRegistry, OBJECT, type);
        var name = key(componentRegistry, type);
        var valueCodec = (Codec<Object>) call(COMPONENT_TYPE, "codec", type, NONE);
        var wireCodec = call(COMPONENT_TYPE, "streamCodec", type, NONE);
        Object value;
        try {
            value = sampleValue(name, valueCodec, wireCodec, samples);
        } catch (Exception e) {
            throw new IllegalStateException("Component " + name, e);
        }

        var payload = Unpooled.buffer();
        Vanilla.call(encode, wireCodec, friendly(payload), value);
        var row = new JsonObject();
        row.addProperty("name", name);
        for (boolean delimited : new boolean[]{false, true}) {
            var frame = Unpooled.buffer();
            for (var number : new int[]{1, 1, 1, 0, id}) {
                writeVarInt(frame, number);
            }
            if (delimited) {
                writeVarInt(frame, payload.readableBytes());
            }
            frame.writeBytes(payload, 0, payload.readableBytes());
            row.addProperty(delimited ? "untrusted" : "wire", ByteBufUtil.hexDump(frame));
            frame.release();
        }

        var hashes = new JsonObject();
        if (valueCodec != null) {
            hashes.addProperty(Integer.toString(id), hash(valueCodec, value));
        }
        row.add("hashes", hashes);
        payload.release();
        return row;
    }

    static Object sampleValue(String name, Codec<Object> valueCodec, Object wireCodec, JsonObject samples) {
        var sample = samples.get(name.substring(10));
        if (valueCodec != null) {
            return valueCodec.parse(ops, sample).getOrThrow();
        }

        // Network-only components decode from a VarInt: the sample's number, or zero.
        var source = Unpooled.buffer();
        var isNumber = sample != null && sample.isJsonPrimitive() && sample.getAsJsonPrimitive().isNumber();
        writeVarInt(source, isNumber ? sample.getAsInt() : 0);
        var value = call("net.minecraft.network.codec.StreamDecoder", "decode", wireCodec, OBJECT, friendly(source));
        source.release();
        return value;
    }

    static JsonArray merchantOffers(JsonElement inputs) {
        var merchantCodec = (Codec<Object>) Vanilla.get("net.minecraft.world.item.trading.MerchantOffer", "CODEC");
        var merchantStream = Vanilla.get("net.minecraft.world.item.trading.MerchantOffer", "STREAM_CODEC");
        var offers = new JsonArray();
        for (var input : inputs.getAsJsonArray()) {
            var offer = merchantCodec.parse(ops, input).getOrThrow();
            var buffer = Unpooled.buffer();
            Vanilla.call(encode, merchantStream, friendly(buffer), offer);
            offers.add(ByteBufUtil.hexDump(buffer));
            buffer.release();
        }
        return offers;
    }
}
