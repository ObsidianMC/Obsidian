import com.google.gson.*;
import com.mojang.serialization.*;
import java.nio.file.*;
import java.util.*;

/** Captures typed provider results and the following random draw from vanilla 26.3.
 * Usage: cases JSON, output JSON. Run with the vanilla server and libraries on the classpath.
 */
public final class ContextNumberFixtures {
    private static Object call(String owner, String name, Object target, String[] types, Object... arguments) {
        var method = Vanilla.method(owner, name, types);
        return Vanilla.call(method, target, arguments);
    }

    @SuppressWarnings("unchecked")
    public static void main(String[] arguments) throws Exception {
        VanillaDumper.bootstrap();
        var access = VanillaDumper.vanillaRegistries();
        var ops = (DynamicOps<JsonElement>) call(
            "net.minecraft.resources.RegistryOps",
            "create",
            null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"},
            JsonOps.INSTANCE,
            access);

        var cases = JsonParser.parseString(Files.readString(Path.of(arguments[0]))).getAsJsonArray();
        var rows = new JsonArray();
        for (var value : cases) {
            for (long seed : new long[]{1, 42}) {
                rows.add(capture(value.getAsJsonObject(), seed, ops, access));
            }
        }

        var output = new GsonBuilder().setPrettyPrinting().create().toJson(rows);
        Files.writeString(Path.of(arguments[1]), output);
    }

    @SuppressWarnings("unchecked")
    private static JsonObject capture(
        JsonObject input,
        long seed,
        DynamicOps<JsonElement> ops,
        Object access) {
        var kind = input.get("kind").getAsString();
        var packageName = "net.minecraft.world.level.storage.loot.providers.number.";
        var className = kind.equals("int") ? "ints.ContextIntProvider" : "floats.ContextFloatProvider";
        var providerClass = packageName + className;
        var codecOwner = providerClass + "s";
        var codec = (Codec<Object>) Vanilla.get(codecOwner, "CODEC");
        var holder = codec.parse(ops, input.get("provider")).getOrThrow();
        var provider = call("net.minecraft.core.Holder", "value", holder, new String[]{});

        // This probe has no ServerLevel/command storage. For missing-storage cases, inspect and evaluate the
        // fallback that vanilla's codec installed, rather than substituting a hand-written expected zero.
        if (input.has("evaluateStorageFallback") && input.get("evaluateStorageFallback").getAsBoolean()) {
            var fallback = call(packageName + (kind.equals("int") ? "ints." : "floats.") + "StorageValue",
                "fallback", provider, new String[]{});
            provider = call("net.minecraft.core.Holder", "value", fallback, new String[]{});
        }

        // Only block state is needed for the built-in fuel and composting predicates.
        var random = call("net.minecraft.util.RandomSource", "create", null, new String[]{"long"}, seed);
        var parameters = new HashMap<Object, Object>();
        if (input.has("block")) {
            var blockCodec = (Codec<Object>) Vanilla.get("net.minecraft.world.level.block.state.BlockState", "CODEC");
            var state = blockCodec.parse(ops, input.get("block")).getOrThrow();
            var key = Vanilla.get("net.minecraft.world.level.storage.loot.parameters.LootContextParams", "BLOCK_STATE");
            parameters.put(key, state);
        }

        var constructor = Vanilla.constructor("net.minecraft.util.context.ContextMap", "java.util.Map");
        var map = Vanilla.create(constructor, parameters);
        var params = Vanilla.create(
            Vanilla.constructor(
                "net.minecraft.world.level.storage.loot.LootParams",
                "net.minecraft.server.level.ServerLevel",
                "net.minecraft.util.context.ContextMap",
                "java.util.Map",
                "float"),
            null,
            map,
            Map.of(),
            0f);
        var context = Vanilla.create(
            Vanilla.constructor(
                "net.minecraft.world.level.storage.loot.LootContext",
                "net.minecraft.world.level.storage.loot.LootParams",
                "net.minecraft.util.RandomSource",
                "net.minecraft.core.HolderGetter$Provider"),
            params,
            random,
            access);

        var row = input.deepCopy();
        row.addProperty("seed", seed);
        var method = kind.equals("int") ? "getInt" : "getFloat";
        var result = (Number) call(
            providerClass,
            method,
            provider,
            new String[]{"net.minecraft.world.level.storage.loot.LootContext"},
            context);
        if (kind.equals("int")) {
            row.addProperty("value", result.intValue());
        } else {
            row.addProperty("bits", Float.floatToIntBits(result.floatValue()));
        }

        row.addProperty("nextInt", (int) call("net.minecraft.util.RandomSource", "nextInt", random, new String[]{}));
        return row;
    }
}
