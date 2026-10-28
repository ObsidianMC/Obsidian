import com.google.gson.*;
import com.mojang.serialization.*;
import java.nio.file.*;
import java.util.*;
import java.util.function.Consumer;

/** Captures entity loot and its next draw from the pinned vanilla jar without a running server.
 * Usage: generated data directory (containing minecraft/loot_table), output JSON.
 * Missing entity parameters represent environmental kills with no fire or equipment; a player marker is used
 * only for killed_by_player (no entity methods are called on it). These captures intentionally use looting zero.
 */
public final class EntityLootFixtures {
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
        capture(rows, arguments[0], "pig", 42, false);
        capture(rows, arguments[0], "cow", 1, false);
        capture(rows, arguments[0], "skeleton", 42, false);
        capture(rows, arguments[0], "spider", 1, true);
        capture(rows, arguments[0], "blaze", 42, false);
        capture(rows, arguments[0], "blaze", 42, true);
        Files.writeString(Path.of(arguments[1]), new GsonBuilder().setPrettyPrinting().create().toJson(rows));
    }

    private static void capture(JsonArray rows, String data, String name, long seed, boolean player) throws Exception {
        var params = new HashMap<Object, Object>();
        if (player) {
            params.put(Vanilla.get("net.minecraft.world.level.storage.loot.parameters.LootContextParams",
                "LAST_DAMAGE_PLAYER"), new Object());
        }

        var map = Vanilla.create(Vanilla.constructor("net.minecraft.util.context.ContextMap",
            "java.util.Map"), params);
        var lootParams = Vanilla.create(Vanilla.constructor(
            "net.minecraft.world.level.storage.loot.LootParams", "net.minecraft.server.level.ServerLevel",
            "net.minecraft.util.context.ContextMap", "java.util.Map", "float"), null, map, Map.of(), 0f);
        var random = call("net.minecraft.util.RandomSource", "create", null, new String[]{"long"}, seed);
        var context = Vanilla.create(Vanilla.constructor(
            "net.minecraft.world.level.storage.loot.LootContext", "net.minecraft.world.level.storage.loot.LootParams",
            "net.minecraft.util.RandomSource", "net.minecraft.core.HolderGetter$Provider"),
            lootParams, random, access);
        var path = Path.of(data, "minecraft", "loot_table", "entities", name + ".json");
        var table = decode("net.minecraft.world.level.storage.loot.LootTable", "DIRECT_CODEC",
            JsonParser.parseString(Files.readString(path)));
        var drops = new JsonArray();
        Consumer<Object> output = stack -> {
            var count = (int) call("net.minecraft.world.item.ItemStack", "getCount", stack, new String[]{});
            if (count <= 0) {
                return;
            }

            var item = call("net.minecraft.world.item.ItemStack", "getItem", stack, new String[]{});
            var id = call("net.minecraft.core.Registry", "getKey",
                Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "ITEM"),
                new String[]{"java.lang.Object"}, item).toString();
            drops.add(id + "*" + count);
        };
        call("net.minecraft.world.level.storage.loot.LootTable", "getRandomItemsRaw", table,
            new String[]{"net.minecraft.world.level.storage.loot.LootContext", "java.util.function.Consumer"},
            context, output);
        var row = new JsonObject();
        row.addProperty("type", name);
        row.addProperty("seed", seed);
        row.addProperty("player", player);
        row.add("drops", drops);
        row.addProperty("nextInt", (int) call("net.minecraft.util.RandomSource", "nextInt", random, new String[]{}));
        rows.add(row);
    }
}
