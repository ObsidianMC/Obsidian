import com.google.gson.*;
import com.mojang.serialization.*;
import java.nio.file.*;
import java.util.*;
import java.util.stream.*;

/**
 * Evaluates vanilla 1.21.11 block loot tables with the real server codecs and loot engine, for block-drop parity
 * fixtures. Each case gives a block state id, an optional tool stack, an optional explosion radius, an origin and a
 * seed. The output repeats each input case with its item stacks in vanilla order ("item*count", plus the copied block state
 * as "{name=value}"), and "tableAbsent" for blocks without a loot table file. No server or world is started.
 */
public class BlockLootFixtures {
    static VanillaDumper.Mojang m;
    static Object call(String owner, String name, Object target, String[] types, Object... args) {
        return VanillaDumper.Mojang.call(m.method(owner, name, types), target, args);
    }
    public static void main(String[] args) throws Exception {
        m = VanillaDumper.Mojang.read(Path.of(args[0]));
        call("net.minecraft.SharedConstants", "tryDetectVersion", null, new String[]{});
        call("net.minecraft.server.Bootstrap", "bootStrap", null, new String[]{});
        var builtin = m.get("net.minecraft.core.registries.BuiltInRegistries", "REGISTRY");
        var base = new ArrayList<Object>();
        ((Iterable<?>)builtin).forEach(base::add);
        var pack = call("net.minecraft.server.packs.repository.ServerPacksSource", "createVanillaPackSource", null, new String[]{});
        var resources = VanillaDumper.Mojang.create(m.constructor("net.minecraft.server.packs.resources.MultiPackResourceManager", "net.minecraft.server.packs.PackType", "java.util.List"), m.get("net.minecraft.server.packs.PackType", "SERVER_DATA"), List.of(pack));
        for (var reg : base) {
            var pending = (Optional<?>)call("net.minecraft.tags.TagLoader", "loadPendingTags", null,
                new String[]{"net.minecraft.server.packs.resources.ResourceManager", "net.minecraft.core.Registry"}, resources, reg);
            if (pending.isPresent()) call("net.minecraft.core.Registry$PendingTags", "apply", pending.get(), new String[]{});
        }
        var dynamic = call("net.minecraft.resources.RegistryDataLoader", "load", null,
            new String[]{"net.minecraft.server.packs.resources.ResourceManager", "java.util.List", "java.util.List"}, resources, base,
            m.get("net.minecraft.resources.RegistryDataLoader", "WORLDGEN_REGISTRIES"));
        var entries = (Stream<?>)call("net.minecraft.core.RegistryAccess", "registries", dynamic, new String[]{});
        entries.forEach(entry -> base.add(call("net.minecraft.core.RegistryAccess$RegistryEntry", "value", entry, new String[]{})));
        var access = VanillaDumper.Mojang.create(m.constructor("net.minecraft.core.RegistryAccess$ImmutableRegistryAccess", "java.util.List"), base);
        // Registers every vanilla block (and its registry holder); block states need their holders to be bound.
        Class.forName(m.type("net.minecraft.world.level.block.Blocks").getName(), true, BlockLootFixtures.class.getClassLoader());
        var ops = (DynamicOps<Object>)call("net.minecraft.resources.RegistryOps", "create", null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"}, JsonOps.INSTANCE, access);
        var tableCodec = (Codec<Object>)m.get("net.minecraft.world.level.storage.loot.LootTable", "DIRECT_CODEC");
        var stackCodec = (Codec<Object>)m.get("net.minecraft.world.item.ItemStack", "CODEC");
        var stateRegistry = m.get("net.minecraft.world.level.block.Block", "BLOCK_STATE_REGISTRY");
        var byId = m.method("net.minecraft.core.IdMapper", "byId", "int");
        var paramSet = m.get("net.minecraft.world.level.storage.loot.parameters.LootContextParamSets", "BLOCK");
        var params = "net.minecraft.world.level.storage.loot.parameters.LootContextParams";
        var keyOrigin = m.get(params, "ORIGIN");
        var keyState = m.get(params, "BLOCK_STATE");
        var keyTool = m.get(params, "TOOL");
        var keyRadius = m.get(params, "EXPLOSION_RADIUS");
        var builderCtor = m.constructor("net.minecraft.world.level.storage.loot.LootParams$Builder", "net.minecraft.server.level.ServerLevel");
        var withParameter = m.method("net.minecraft.world.level.storage.loot.LootParams$Builder", "withParameter", "net.minecraft.util.context.ContextKey", "java.lang.Object");
        var createParams = m.method("net.minecraft.world.level.storage.loot.LootParams$Builder", "create", "net.minecraft.util.context.ContextKeySet");
        var vec3Ctor = m.constructor("net.minecraft.world.phys.Vec3", "double", "double", "double");
        var lootContextCtor = m.constructor("net.minecraft.world.level.storage.loot.LootContext",
            "net.minecraft.world.level.storage.loot.LootParams", "net.minecraft.util.RandomSource", "net.minecraft.core.HolderGetter$Provider");
        var createRandom = m.method("net.minecraft.util.RandomSource", "create", "long");
        var isEmpty = m.method("net.minecraft.world.item.ItemStack", "isEmpty");
        var getRandomItemsCtx = m.method("net.minecraft.world.level.storage.loot.LootTable", "getRandomItems",
            "net.minecraft.world.level.storage.loot.LootContext", "java.util.function.Consumer");

        // Loot splits stacks by the level's enabled feature flags, which the level reads from its server. Headless
        // instances are allocated without running a server, with only those members set; no other members are used.
        var unsafeClass = Class.forName("sun.misc.Unsafe");
        var unsafeField = unsafeClass.getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = unsafeField.get(null);
        var allocate = unsafeClass.getMethod("allocateInstance", Class.class);
        var serverType = m.type("net.minecraft.server.dedicated.DedicatedServer");
        var serverLevelType = m.type("net.minecraft.server.level.ServerLevel");
        var flagSetType = m.type("net.minecraft.world.flag.FeatureFlagSet");
        var vanillaFlags = m.get("net.minecraft.world.flag.FeatureFlags", "VANILLA_SET");
        var server = allocate.invoke(unsafe, serverType);
        for (var type = serverType; type != null; type = type.getSuperclass())
            for (var field : type.getDeclaredFields())
                if (field.getType() == flagSetType && !java.lang.reflect.Modifier.isStatic(field.getModifiers())) {
                    field.setAccessible(true);
                    field.set(server, vanillaFlags);
                }
        var worldDataType = m.type("net.minecraft.world.level.storage.WorldData");
        var worldData = java.lang.reflect.Proxy.newProxyInstance(BlockLootFixtures.class.getClassLoader(),
            new Class<?>[]{worldDataType}, (proxy, method, arguments) -> {
                if (method.getReturnType() == flagSetType) return vanillaFlags;
                throw new UnsupportedOperationException("headless WorldData: " + method);
            });
        for (var type = serverType; type != null; type = type.getSuperclass())
            for (var field : type.getDeclaredFields())
                if (field.getType() == worldDataType && !java.lang.reflect.Modifier.isStatic(field.getModifiers())) {
                    field.setAccessible(true);
                    field.set(server, worldData);
                }
        var level = allocate.invoke(unsafe, serverLevelType);
        for (var type = serverLevelType; type != null; type = type.getSuperclass())
            for (var field : type.getDeclaredFields())
                if (field.getType() == m.type("net.minecraft.server.MinecraftServer") && !java.lang.reflect.Modifier.isStatic(field.getModifiers())) {
                    field.setAccessible(true);
                    field.set(level, server);
                }

        var input = JsonParser.parseString(Files.readString(Path.of(args[1]))).getAsJsonArray();
        var tablesDir = Path.of(args[2]);
        var output = new JsonArray();
        for (var element : input) {
            var c = element.getAsJsonObject();
            // The fixture row is the input case plus the vanilla stacks; the input's own fields carry over unchanged.
            var merged = JsonParser.parseString(element.toString()).getAsJsonObject();
            merged.remove("unsupportedHeadless");
            try {
                var tableId = c.get("table").getAsString();
                var file = tablesDir.resolve(tableId.substring("minecraft:blocks/".length()) + ".json");
                // Vanilla has no loot table file for some blocks, which drop nothing.
            if (!Files.exists(file)) {
                merged.add("stacks", new JsonArray());
                merged.addProperty("tableAbsent", true);
                output.add(merged);
                continue;
            }
            var table = tableCodec.parse(ops, JsonParser.parseString(Files.readString(file))).getOrThrow();

                var builder = VanillaDumper.Mojang.create(builderCtor, level);
                var state = VanillaDumper.Mojang.call(byId, stateRegistry, c.get("state").getAsInt());
                builder = VanillaDumper.Mojang.call(withParameter, builder, keyState, state);
                var origin = c.getAsJsonArray("origin");
                var vec = VanillaDumper.Mojang.create(vec3Ctor, origin.get(0).getAsDouble(), origin.get(1).getAsDouble(), origin.get(2).getAsDouble());
                builder = VanillaDumper.Mojang.call(withParameter, builder, keyOrigin, vec);
                // Vanilla's block parameter set requires TOOL; bare hands are ItemStack.EMPTY.
                var tool = c.has("tool") ? stackCodec.parse(ops, c.get("tool")).getOrThrow()
                    : m.get("net.minecraft.world.item.ItemStack", "EMPTY");
                builder = VanillaDumper.Mojang.call(withParameter, builder, keyTool, tool);
                if (c.has("explosionRadius"))
                    builder = VanillaDumper.Mojang.call(withParameter, builder, keyRadius, (float)c.get("explosionRadius").getAsDouble());
                var lootParams = VanillaDumper.Mojang.call(createParams, builder, paramSet);

                // A stored seed: vanilla's RandomSource.create(seed), the same source the container fixtures use.
                // The constructor used by vanilla's builder, which otherwise needs a running server to look up the random source.
                var random = VanillaDumper.Mojang.call(createRandom, null, c.get("seed").getAsLong());
                var lootContext = VanillaDumper.Mojang.create(lootContextCtor, lootParams, random, access);
                var stacks = new JsonArray();
                var consumer = java.lang.reflect.Proxy.newProxyInstance(BlockLootFixtures.class.getClassLoader(),
                    new Class<?>[]{java.util.function.Consumer.class}, (proxy, method, arguments) -> {
                        // Block drops discard emptied stacks (vanilla's Block.popResource skips them).
                        if (method.getName().equals("accept") && !(Boolean)VanillaDumper.Mojang.call(isEmpty, arguments[0])) {
                            JsonElement json = (JsonElement)stackCodec.encodeStart(ops, arguments[0]).getOrThrow();
                            var obj = json.getAsJsonObject();
                            var line = obj.get("id").getAsString() + "*" + obj.get("count").getAsInt();
                            // Copied block state, as "{name=value,...}" in name order.
                            if (obj.has("components") && obj.getAsJsonObject("components").has("minecraft:block_state")) {
                                var properties = new java.util.TreeMap<String, String>();
                                for (var entry : obj.getAsJsonObject("components").getAsJsonObject("minecraft:block_state").entrySet())
                                    properties.put(entry.getKey(), entry.getValue().getAsString());
                                line += " " + properties.entrySet().stream()
                                    .map(entry -> entry.getKey() + "=" + entry.getValue())
                                    .collect(java.util.stream.Collectors.joining(",", "{", "}"));
                            }
                            stacks.add(line);
                        }
                        return null;
                    });
                VanillaDumper.Mojang.call(getRandomItemsCtx, table, lootContext, consumer);

                merged.add("stacks", stacks);
                output.add(merged);
            } catch (Exception e) {
                // Only cases marked as unsupported headless may be skipped, and say why. Anything else is a failure: the
                // fixture must not shrink without anyone noticing.
                if (!c.has("unsupportedHeadless")) throw e;
                Throwable cause = e;
                while (cause.getCause() != null) cause = cause.getCause();
                System.err.println("Skipped " + c.get("name").getAsString() + ": " + c.get("unsupportedHeadless").getAsString() + " (" + cause + ")");
            }
        }
        Files.writeString(Path.of(args[3]), new GsonBuilder().setPrettyPrinting().create().toJson(output));
    }
}
