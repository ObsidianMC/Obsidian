import com.google.gson.*;
import com.mojang.serialization.*;
import com.google.common.hash.HashCode;
import io.netty.buffer.*;
import java.nio.file.*;
import java.util.*;
import java.util.stream.*;

/** Captures wire bytes and HashOps results by running the actual vanilla codecs. No server is started. */
public class ComponentFixtures {
    static Object call(String owner, String name, Object target, String[] types, Object... args) {
        return Vanilla.call(Vanilla.method(owner, name, types), target, args);
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
        var access = Vanilla.create(Vanilla.constructor("net.minecraft.core.RegistryAccess$ImmutableRegistryAccess", "java.util.List"), base);
        VanillaDumper.bindComponents(access);
        var ops = (DynamicOps<Object>)call("net.minecraft.resources.RegistryOps", "create", null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"}, JsonOps.INSTANCE, access);
        var hashOps = (DynamicOps<Object>)call("net.minecraft.resources.RegistryOps", "create", null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"}, Vanilla.get("net.minecraft.util.HashOps", "CRC32C_INSTANCE"), access);
        var codec = (Codec<Object>)Vanilla.get("net.minecraft.world.item.ItemStack", "CODEC");
        var stream = Vanilla.get("net.minecraft.world.item.ItemStack", "OPTIONAL_STREAM_CODEC");
        var untrusted = Vanilla.get("net.minecraft.world.item.ItemStack", "OPTIONAL_UNTRUSTED_STREAM_CODEC");
        var encode = Vanilla.method("net.minecraft.network.codec.StreamEncoder", "encode", "java.lang.Object", "java.lang.Object");
        var componentRegistry = Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "DATA_COMPONENT_TYPE");
        var output = new JsonObject();
        var registry = new JsonArray();
        for (var type : (Iterable<?>)componentRegistry) registry.add(call("net.minecraft.core.Registry", "getKey", componentRegistry, new String[]{"java.lang.Object"}, type).toString());
        output.add("componentTypes", registry);
        var registryNames = new JsonObject();
        for (var reg : base) {
            var names = new JsonArray();
            for (var value : (Iterable<?>)reg) names.add(call("net.minecraft.core.Registry", "getKey", reg, new String[]{"java.lang.Object"}, value).toString());
            var key = call("net.minecraft.core.Registry", "key", reg, new String[]{});
            registryNames.add(call("net.minecraft.resources.ResourceKey", "identifier", key, new String[]{}).toString(), names);
        }
        // Only registries used by the hash tests are included; their order is connection-specific in real clients.
        var hashRegistries = new JsonObject();
        for (var key : List.of("minecraft:attribute", "minecraft:enchantment", "minecraft:potion", "minecraft:mob_effect", "minecraft:banner_pattern", "minecraft:trim_material", "minecraft:trim_pattern", "minecraft:instrument", "minecraft:sound_event", "minecraft:entity_type", "minecraft:block_transformer", "minecraft:decorated_pot_pattern", "minecraft:cat_sound_variant", "minecraft:chicken_sound_variant", "minecraft:cow_sound_variant", "minecraft:pig_sound_variant", "minecraft:damage_type")) hashRegistries.add(key, registryNames.get(key));
        // Tool fixture uses stone (ID 1); retaining the prefix avoids committing the entire block registry.
        var blocks = new JsonArray();
        for (int id = 0; id <= 1; id++) blocks.add(registryNames.getAsJsonArray("minecraft:block").get(id));
        hashRegistries.add("minecraft:block", blocks);
        output.add("registries", hashRegistries);
        // Persistent item codecs need names for these holders even when they arrived as network IDs.
        var componentRegistries = new JsonObject();
        for (var key : List.of("minecraft:banner_pattern", "minecraft:jukebox_song", "minecraft:damage_type")) {
            componentRegistries.add(key, registryNames.get(key));
        }
        Files.writeString(Path.of(args[1]).resolveSibling("component_registry_ids.json"),
            new GsonBuilder().setPrettyPrinting().create().toJson(componentRegistries) + "\n");
        var cases = new JsonArray();
        for (var input : JsonParser.parseString(Files.readString(Path.of(args[0]))).getAsJsonArray()) {
            var obj = input.getAsJsonObject();
            Object stack;
            try { stack = codec.parse(ops, obj.get("stack")).getOrThrow(); }
            catch (Exception e) { throw new IllegalStateException("Fixture " + obj.get("name"), e); }
            var row = new JsonObject(); row.add("name", obj.get("name"));
            for (var variant : Map.of("wire", stream, "untrusted", untrusted).entrySet()) {
                var buf = Unpooled.buffer();
                var friendly = Vanilla.create(Vanilla.constructor("net.minecraft.network.RegistryFriendlyByteBuf", "io.netty.buffer.ByteBuf", "net.minecraft.core.RegistryAccess"), buf, access);
                Vanilla.call(encode, variant.getValue(), friendly, stack);
                row.addProperty(variant.getKey(), ByteBufUtil.hexDump(buf)); buf.release();
            }
            var generator = java.lang.reflect.Proxy.newProxyInstance(ComponentFixtures.class.getClassLoader(),
                new Class<?>[]{Vanilla.type("net.minecraft.network.HashedPatchMap$HashGenerator")}, (proxy, method, arguments) -> {
                    var result = (DataResult<?>)call("net.minecraft.core.component.TypedDataComponent", "encodeValue", arguments[0],
                        new String[]{"com.mojang.serialization.DynamicOps"}, hashOps);
                    return ((HashCode)result.getOrThrow()).asInt();
                });
            var hashed = call("net.minecraft.network.HashedStack", "create", null,
                new String[]{"net.minecraft.world.item.ItemStack", "net.minecraft.network.HashedPatchMap$HashGenerator"}, stack, generator);
            var hashBuffer = Unpooled.buffer();
            var hashFriendly = Vanilla.create(Vanilla.constructor("net.minecraft.network.RegistryFriendlyByteBuf", "io.netty.buffer.ByteBuf", "net.minecraft.core.RegistryAccess"), hashBuffer, access);
            Vanilla.call(encode, Vanilla.get("net.minecraft.network.HashedStack", "STREAM_CODEC"), hashFriendly, hashed);
            row.addProperty("hashed", ByteBufUtil.hexDump(hashBuffer)); hashBuffer.release();
            var patch = call("net.minecraft.world.item.ItemStack", "getComponentsPatch", stack, new String[]{});
            var hashes = new JsonObject();
            // The patch's added components; removed ones have no value to hash.
            var added = call("net.minecraft.core.component.DataComponentPatch$SplitResult", "added",
                call("net.minecraft.core.component.DataComponentPatch", "split", patch, new String[]{}), new String[]{});
            for (var component : (Iterable<?>)added) {
                var type = call("net.minecraft.core.component.TypedDataComponent", "type", component, new String[]{});
                var typeCodec = (Codec<Object>)call("net.minecraft.core.component.DataComponentType", "codec", type, new String[]{});
                if (typeCodec == null) continue;
                var id = (int)call("net.minecraft.core.Registry", "getId", componentRegistry, new String[]{"java.lang.Object"}, type);
                var value = call("net.minecraft.core.component.TypedDataComponent", "value", component, new String[]{});
                hashes.addProperty(Integer.toString(id), ((HashCode)typeCodec.encodeStart(hashOps, value).getOrThrow()).asInt());
            }
            row.add("hashes", hashes); cases.add(row);
        }
        var samples = JsonParser.parseString(Files.readString(Path.of(args[0]).resolveSibling("component-samples.json"))).getAsJsonObject();
        var ids = Vanilla.method("net.minecraft.core.Registry", "getId", "java.lang.Object");
        for (var type : (Iterable<?>)componentRegistry) {
            var id = (int)Vanilla.call(ids, componentRegistry, type);
            var name = call("net.minecraft.core.Registry", "getKey", componentRegistry, new String[]{"java.lang.Object"}, type).toString();
            var valueCodec = (Codec<Object>)call("net.minecraft.core.component.DataComponentType", "codec", type, new String[]{});
            var wireCodec = call("net.minecraft.core.component.DataComponentType", "streamCodec", type, new String[]{});
            Object value;
            try {
                if (valueCodec != null) value = valueCodec.parse(ops, samples.get(name.substring(10))).getOrThrow();
                else {
                    var source = Unpooled.buffer();
                    var sample = samples.get(name.substring(10));
                    call("net.minecraft.network.VarInt", "write", null, new String[]{"io.netty.buffer.ByteBuf", "int"},
                        source, sample != null && sample.isJsonPrimitive() && sample.getAsJsonPrimitive().isNumber()
                            ? sample.getAsInt() : 0);
                    var friendly = Vanilla.create(Vanilla.constructor("net.minecraft.network.RegistryFriendlyByteBuf", "io.netty.buffer.ByteBuf", "net.minecraft.core.RegistryAccess"), source, access);
                    value = call("net.minecraft.network.codec.StreamDecoder", "decode", wireCodec, new String[]{"java.lang.Object"}, friendly);
                    source.release();
                }
            } catch (Exception e) { throw new IllegalStateException("Component " + name, e); }
            var payload = Unpooled.buffer();
            var friendly = Vanilla.create(Vanilla.constructor("net.minecraft.network.RegistryFriendlyByteBuf", "io.netty.buffer.ByteBuf", "net.minecraft.core.RegistryAccess"), payload, access);
            Vanilla.call(encode, wireCodec, friendly, value);
            var row = new JsonObject(); row.addProperty("name", name);
            for (boolean delimited : new boolean[]{false, true}) {
                var frame = Unpooled.buffer();
                for (var number : new int[]{1, 1, 1, 0, id}) call("net.minecraft.network.VarInt", "write", null, new String[]{"io.netty.buffer.ByteBuf", "int"}, frame, number);
                if (delimited) call("net.minecraft.network.VarInt", "write", null, new String[]{"io.netty.buffer.ByteBuf", "int"}, frame, payload.readableBytes());
                frame.writeBytes(payload, 0, payload.readableBytes());
                row.addProperty(delimited ? "untrusted" : "wire", ByteBufUtil.hexDump(frame)); frame.release();
            }
            var hashes = new JsonObject();
            if (valueCodec != null) hashes.addProperty(Integer.toString(id), ((HashCode)valueCodec.encodeStart(hashOps, value).getOrThrow()).asInt());
            row.add("hashes", hashes); cases.add(row); payload.release();
        }
        output.add("cases", cases);
        var merchantCodec = (Codec<Object>)Vanilla.get("net.minecraft.world.item.trading.MerchantOffer", "CODEC");
        var merchantStream = Vanilla.get("net.minecraft.world.item.trading.MerchantOffer", "STREAM_CODEC");
        var offers = new JsonArray();
        for (var input : JsonParser.parseString(Files.readString(Path.of(args[0]).resolveSibling("merchant-fixtures-input.json"))).getAsJsonArray()) {
            var offer = merchantCodec.parse(ops, input).getOrThrow();
            var buffer = Unpooled.buffer();
            var friendly = Vanilla.create(Vanilla.constructor("net.minecraft.network.RegistryFriendlyByteBuf", "io.netty.buffer.ByteBuf", "net.minecraft.core.RegistryAccess"), buffer, access);
            Vanilla.call(encode, merchantStream, friendly, offer);
            offers.add(ByteBufUtil.hexDump(buffer)); buffer.release();
        }
        output.add("merchantOffers", offers);
        Files.writeString(Path.of(args[1]), new GsonBuilder().setPrettyPrinting().disableHtmlEscaping().create().toJson(output) + "\n");
        System.out.println("Captured " + cases.size() + " item stacks and " + registry.size() + " component IDs.");
    }
}
