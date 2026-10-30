import com.google.gson.*;
import com.mojang.serialization.*;
import java.nio.file.*;
import java.util.*;
import java.util.function.Consumer;

/**
 * Captures block loot and the next random draw from vanilla without starting a server.
 * Usage: generated vanilla data directory (containing minecraft/loot_table), output JSON.
 * Use Java 25's source launcher with the vanilla jar and libraries on the classpath; the sibling dumper supplies
 * the bootstrap. Its source class loader keeps the probe separate from the signed game jar.
 */
public final class BlockLootFixtures {
    private static DynamicOps<JsonElement> ops;
    private static Object access;

    private static Object call(String owner, String name, Object target, String[] types, Object... arguments) {
        return Vanilla.call(Vanilla.method(owner, name, types), target, arguments);
    }

    @SuppressWarnings("unchecked")
    private static Object decode(String owner, String field, JsonElement json) {
        return ((Codec<Object>) Vanilla.get(owner, field)).parse(ops, json).getOrThrow();
    }

    @SuppressWarnings("unchecked")
    public static void main(String[] arguments) throws Exception {
        VanillaDumper.bootstrap();
        access = VanillaDumper.vanillaRegistries();
        ops = (DynamicOps<JsonElement>) call("net.minecraft.resources.RegistryOps", "create", null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"},
            JsonOps.INSTANCE, access);
        var rows = new JsonArray();
        capture(rows, arguments[0], "stone", "air", null, 0, 1, null, true, Map.of());
        capture(rows, arguments[0], "stone", "wooden_pickaxe", null, 0, 1, null, true, Map.of());
        capture(rows, arguments[0], "stone", "diamond_pickaxe", "silk_touch", 1, 1, null, true, Map.of());
        capture(rows, arguments[0], "diamond_ore", "wooden_pickaxe", null, 0, 1, null, true, Map.of());
        capture(rows, arguments[0], "diamond_ore", "diamond_pickaxe", "fortune", 3, 1, null, true, Map.of());
        capture(rows, arguments[0], "diamond_ore", "diamond_pickaxe", "fortune", 3, 42, null, true, Map.of());
        capture(rows, arguments[0], "diamond_ore", "diamond_pickaxe", "fortune", 3, 5, null, true, Map.of());
        capture(rows, arguments[0], "redstone_ore", "diamond_pickaxe", "fortune", 3, 1, null, true, Map.of());
        capture(rows, arguments[0], "glass", "diamond_pickaxe", null, 0, 1, null, true, Map.of());
        capture(rows, arguments[0], "glass", "diamond_pickaxe", "silk_touch", 1, 1, null, true, Map.of());
        capture(rows, arguments[0], "oak_door", "air", null, 0, 1, null, true, Map.of("half", "lower"));
        capture(rows, arguments[0], "oak_door", "air", null, 0, 1, null, true, Map.of("half", "upper"));
        capture(rows, arguments[0], "wheat", "air", null, 0, 1, null, true, Map.of("age", "0"));
        capture(rows, arguments[0], "wheat", "diamond_hoe", "fortune", 3, 1, null, true, Map.of("age", "7"));
        capture(rows, arguments[0], "oak_leaves", "air", null, 0, 42, null, true, Map.of());
        capture(rows, arguments[0], "wall_torch", "air", null, 0, 1, null, true, Map.of());
        capture(rows, arguments[0], "stone", "air", null, 0, 1, 4f, false, Map.of());
        capture(rows, arguments[0], "stone", "air", null, 0, 42, 4f, false, Map.of());
        capture(rows, arguments[0], "redstone_ore", "air", null, 0, 42, 4f, false, Map.of());
        capture(rows, arguments[0], "redstone_ore", "air", null, 0, 1, null, false, Map.of());
        capture(rows, arguments[0], "poplar_log", "air", null, 0, 1, null, true, Map.of());
        capture(rows, arguments[0], "poplar_slab", "air", null, 0, 1, null, true, Map.of("type", "double"));
        capture(rows, arguments[0], "white_wool_slab", "air", null, 0, 1, null, true, Map.of("type", "double"));
        capture(rows, arguments[0], "straw_bed", "air", null, 0, 1, null, true, Map.of("part", "head"));
        Files.writeString(Path.of(arguments[1]), new GsonBuilder().setPrettyPrinting().create().toJson(rows));
    }

    private static void capture(JsonArray rows, String data, String block, String item, String enchantment,
        int level, long seed, Float radius, boolean checkTool, Map<String, String> properties) throws Exception {
        var stateJson = new JsonObject();
        stateJson.addProperty("id", "minecraft:" + block);
        stateJson.add("properties", new Gson().toJsonTree(properties));
        var state = decode("net.minecraft.world.level.block.state.BlockState", "CODEC", stateJson);
        var toolJson = new JsonObject();
        toolJson.addProperty("id", "minecraft:" + item);
        if (enchantment != null) {
            var enchantments = new JsonObject();
            enchantments.addProperty("minecraft:" + enchantment, level);
            var components = new JsonObject();
            components.add("minecraft:enchantments", enchantments);
            toolJson.add("components", components);
        }

        var tool = item.equals("air") ? Vanilla.get("net.minecraft.world.item.ItemStack", "EMPTY")
            : decode("net.minecraft.world.item.ItemStack", "CODEC", toolJson);
        var random = call("net.minecraft.util.RandomSource", "create", null, new String[]{"long"}, seed);
        var correct = !(boolean) call("net.minecraft.world.level.block.state.BlockBehaviour$BlockStateBase",
            "requiresCorrectToolForDrops", state, new String[]{})
            || (boolean) call("net.minecraft.world.item.ItemStack", "isCorrectToolForDrops", tool,
                new String[]{"net.minecraft.world.level.block.state.BlockState"}, state);
        var params = new HashMap<Object, Object>();
        params.put(Vanilla.get("net.minecraft.world.level.storage.loot.parameters.LootContextParams", "TOOL"), tool);
        params.put(Vanilla.get("net.minecraft.world.level.storage.loot.parameters.LootContextParams",
            "BLOCK_STATE"), state);
        if (radius != null) {
            params.put(Vanilla.get("net.minecraft.world.level.storage.loot.parameters.LootContextParams",
                "EXPLOSION_RADIUS"), radius);
        }

        var map = Vanilla.create(Vanilla.constructor("net.minecraft.util.context.ContextMap",
            "java.util.Map"), params);
        var lootParams = Vanilla.create(Vanilla.constructor(
            "net.minecraft.world.level.storage.loot.LootParams", "net.minecraft.server.level.ServerLevel",
            "net.minecraft.util.context.ContextMap", "java.util.Map", "float"), null, map, Map.of(), 0f);
        var context = Vanilla.create(Vanilla.constructor(
            "net.minecraft.world.level.storage.loot.LootContext", "net.minecraft.world.level.storage.loot.LootParams",
            "net.minecraft.util.RandomSource", "net.minecraft.core.HolderGetter$Provider"),
            lootParams, random, access);
        var result = new JsonArray();
        var owner = call("net.minecraft.world.level.block.state.BlockBehaviour$BlockStateBase", "getBlock", state,
            new String[]{});
        var key = (Optional<?>) call("net.minecraft.world.level.block.state.BlockBehaviour", "getLootTable", owner,
            new String[]{});
        if ((!checkTool || correct) && key.isPresent()) {
            var id = call("net.minecraft.resources.ResourceKey", "identifier", key.get(), new String[]{}).toString();
            var path = Path.of(data, "minecraft", "loot_table", id.substring(id.indexOf(':') + 1) + ".json");
            var table = decode("net.minecraft.world.level.storage.loot.LootTable", "DIRECT_CODEC",
                JsonParser.parseString(Files.readString(path)));
            Consumer<Object> output = stack -> {
                var count = (int) call("net.minecraft.world.item.ItemStack", "getCount", stack, new String[]{});
                if (count <= 0) {
                    return;
                }

                var dropped = call("net.minecraft.world.item.ItemStack", "getItem", stack, new String[]{});
                var name = call("net.minecraft.core.Registry", "getKey",
                    Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "ITEM"),
                    new String[]{"java.lang.Object"}, dropped).toString();
                result.add(name + "*" + count);
            };
            call("net.minecraft.world.level.storage.loot.LootTable", "getRandomItemsRaw", table,
                new String[]{"net.minecraft.world.level.storage.loot.LootContext", "java.util.function.Consumer"},
                context, output);
        }

        var row = new JsonObject();
        row.addProperty("block", block);
        row.addProperty("tool", item);
        row.addProperty("enchantment", enchantment);
        row.addProperty("level", level);
        row.addProperty("seed", seed);
        row.addProperty("radius", radius);
        row.addProperty("checkTool", checkTool);
        row.add("properties", new Gson().toJsonTree(properties));
        row.add("drops", result);
        row.addProperty("nextInt", (int) call("net.minecraft.util.RandomSource", "nextInt", random, new String[]{}));
        rows.add(row);
    }
}
