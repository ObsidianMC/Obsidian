import com.google.gson.*;
import com.mojang.serialization.*;
import java.nio.file.*;
import java.util.*;
import java.util.function.Consumer;

/** Captures 26.3 container loot using vanilla's evaluation, slot shuffle and stack splitting.
 * Usage: generated data directory, output JSON. No world is attached, so explorer maps have no destination.
 */
public final class ContainerLootFixtures {
    private record Case(String table, long seed) {}

    private static final Case[] CASES = {
        new Case("chests/simple_dungeon", 1),
        new Case("chests/shipwreck_map", -1),
        new Case("archaeology/desert_well", 5086654115216342560L),
        new Case("chests/trial_chambers/reward_ominous", -6169532649852302182L),
        new Case("chests/trial_chambers/reward_ominous_rare", -7912908803613548926L),
        new Case("chests/simple_dungeon", 0),
        new Case("chests/ancient_city", -1),
        new Case("chests/bastion_treasure", 1),
        new Case("chests/pillager_outpost", 1),
        new Case("chests/abandoned_camp_common_chest", 42),
        new Case("chests/abandoned_camp_secret_chest", 42)
    };

    private static Object call(String owner, String name, Object target, String[] types, Object... arguments) {
        var method = Vanilla.method(owner, name, types);
        return Vanilla.call(method, target, arguments);
    }

    @SuppressWarnings("unchecked")
    public static void main(String[] arguments) throws Exception {
        VanillaDumper.bootstrap();
        var access = VanillaDumper.vanillaRegistries();
        bindEnchantmentTags(arguments[0], access);
        var ops = (DynamicOps<JsonElement>) call(
            "net.minecraft.resources.RegistryOps",
            "create",
            null,
            new String[]{"com.mojang.serialization.DynamicOps", "net.minecraft.core.HolderLookup$Provider"},
            JsonOps.INSTANCE,
            access);

        var rows = new JsonArray();
        for (var entry : CASES) {
            rows.add(capture(entry, arguments[0], ops, access));
        }

        var output = new GsonBuilder().setPrettyPrinting().create().toJson(rows);
        Files.writeString(Path.of(arguments[1]), output);
    }

    @SuppressWarnings("unchecked")
    private static JsonObject capture(
        Case entry,
        String data,
        DynamicOps<JsonElement> ops,
        Object access) throws Exception {
        var file = Path.of(data, "minecraft", "loot_table", entry.table() + ".json");
        var definition = JsonParser.parseString(Files.readString(file));
        var codec = (Codec<Object>) Vanilla.get("net.minecraft.world.level.storage.loot.LootTable", "DIRECT_CODEC");
        expandOptions(definition, data);
        var table = codec.parse(ops, definition).getOrThrow();
        var random = random(entry);
        var context = context(random, access);

        // Invoke vanilla's operations directly; the server-level builder is unnecessary for this fixed context.
        var items = new ArrayList<Object>();
        Consumer<Object> output = items::add;
        call(
            "net.minecraft.world.level.storage.loot.LootTable",
            "getRandomItemsRaw",
            table,
            new String[]{"net.minecraft.world.level.storage.loot.LootContext", "java.util.function.Consumer"},
            context,
            output);
        var stacks = Vanilla.create(
            Vanilla.constructor("it.unimi.dsi.fastutil.objects.ObjectArrayList", "java.util.Collection"),
            items);
        var container = Vanilla.create(Vanilla.constructor("net.minecraft.world.SimpleContainer", "int"), 27);
        var slots = (List<Integer>) call(
            "net.minecraft.world.level.storage.loot.LootTable",
            "getAvailableSlots",
            table,
            new String[]{"net.minecraft.world.Container", "net.minecraft.util.RandomSource"},
            container,
            random);
        call(
            "net.minecraft.world.level.storage.loot.LootTable",
            "shuffleAndSplitItems",
            table,
            new String[]{"it.unimi.dsi.fastutil.objects.ObjectArrayList", "int", "net.minecraft.util.RandomSource"},
            stacks,
            slots.size(),
            random);

        // Serialize the resulting slot contents with vanilla's item codec, including generated component patches.
        var stackCodec = (Codec<Object>) Vanilla.get("net.minecraft.world.item.ItemStack", "CODEC");
        var contents = new TreeMap<Integer, JsonElement>();
        for (var stack : (List<?>) stacks) {
            var count = (int) call("net.minecraft.world.item.ItemStack", "getCount", stack, new String[]{});
            var maximum = (int) call("net.minecraft.world.item.ItemInstance", "getMaxStackSize", stack, new String[]{});
            if (count > maximum) {
                throw new IllegalStateException("Capture needs vanilla's oversized-stack splitter");
            }

            var slot = slots.removeLast();
            contents.put(slot, stackCodec.encodeStart(ops, stack).getOrThrow());
        }

        var row = new JsonObject();
        row.addProperty("table", "minecraft:" + entry.table());
        row.addProperty("seed", entry.seed());
        row.add("slots", new Gson().toJsonTree(contents));
        row.addProperty("nextInt", (int) call("net.minecraft.util.RandomSource", "nextInt", random, new String[]{}));
        return row;
    }

    private static void bindEnchantmentTags(String data, Object access) throws Exception {
        var registry = Vanilla.get("net.minecraft.core.registries.Registries", "ENCHANTMENT");
        var lookup = call(
            "net.minecraft.core.HolderLookup$Provider",
            "lookupOrThrow",
            access,
            new String[]{"net.minecraft.resources.ResourceKey"},
            registry);
        var stream = (java.util.stream.Stream<?>) call(
            "net.minecraft.core.HolderLookup", "listElements", lookup, new String[]{});
        var holders = stream.toList();
        var membership = new HashMap<String, List<Object>>();
        var folder = Path.of(data, "minecraft", "tags", "enchantment");
        try (var files = Files.walk(folder)) {
            for (var file : files.filter(path -> path.toString().endsWith(".json")).toList()) {
                var relative = folder.relativize(file).toString().replace('\\', '/');
                var id = "minecraft:" + relative.substring(0, relative.length() - 5);
                var name = call(
                    "net.minecraft.resources.Identifier", "parse", null, new String[]{"java.lang.String"}, id);
                var key = call(
                    "net.minecraft.tags.TagKey",
                    "create",
                    null,
                    new String[]{"net.minecraft.resources.ResourceKey", "net.minecraft.resources.Identifier"},
                    registry,
                    name);
                var entries = new LinkedHashSet<String>();
                readTag(data, "enchantment", "#" + id, entries);
                for (var entry : entries) {
                    membership.computeIfAbsent(entry, ignored -> new ArrayList<>()).add(key);
                }
            }
        }

        for (var holder : holders) {
            var key = call("net.minecraft.core.Holder$Reference", "key", holder, new String[]{});
            var name = call("net.minecraft.resources.ResourceKey", "identifier", key, new String[]{}).toString();
            var tags = membership.getOrDefault(name, List.of());
            call(
                "net.minecraft.core.Holder$Reference",
                "bindTags",
                holder,
                new String[]{"java.util.Collection"},
                tags);
        }
    }

    private static void expandOptions(JsonElement value, String data) throws Exception {
        if (value.isJsonArray()) {
            for (var element : value.getAsJsonArray()) {
                expandOptions(element, data);
            }
            return;
        }

        if (!value.isJsonObject()) {
            return;
        }

        var object = value.getAsJsonObject();
        var options = object.get("options");
        if (options != null && options.isJsonPrimitive() && options.getAsString().startsWith("#")) {
            var type = object.get("type").getAsString();
            var registry = type.equals("minecraft:set_instrument") ? "instrument" : "enchantment";
            var entries = new LinkedHashSet<String>();
            readTag(data, registry, options.getAsString(), entries);
            object.add("options", new Gson().toJsonTree(entries));
        }

        for (var property : object.entrySet()) {
            expandOptions(property.getValue(), data);
        }
    }

    private static void readTag(String data, String registry, String id, Set<String> entries) throws Exception {
        var path = id.substring(id.indexOf(':') + 1);
        var file = Path.of(data, "minecraft", "tags", registry, path + ".json");
        var tag = JsonParser.parseString(Files.readString(file)).getAsJsonObject();
        for (var value : tag.getAsJsonArray("values")) {
            var name = value.getAsString();
            if (name.startsWith("#")) {
                readTag(data, registry, name, entries);
            } else {
                entries.add(name);
            }
        }
    }

    private static Object random(Case entry) {
        if (entry.seed() != 0) {
            return call("net.minecraft.util.RandomSource", "create", null, new String[]{"long"}, entry.seed());
        }

        var id = call(
            "net.minecraft.resources.Identifier",
            "parse",
            null,
            new String[]{"java.lang.String"},
            "minecraft:" + entry.table());
        var sequence = Vanilla.create(
            Vanilla.constructor("net.minecraft.world.RandomSequence", "long", "net.minecraft.resources.Identifier"),
            12345L,
            id);
        return call("net.minecraft.world.RandomSequence", "random", sequence, new String[]{});
    }

    private static Object emptyLevel() throws Exception {
        // Enchanting with explicit options only reads registryAccess. World construction is unrelated to the capture.
        var unsafeField = sun.misc.Unsafe.class.getDeclaredField("theUnsafe");
        unsafeField.setAccessible(true);
        var unsafe = (sun.misc.Unsafe) unsafeField.get(null);
        var level = unsafe.allocateInstance(Vanilla.type("net.minecraft.server.level.ServerLevel"));
        var registryField = Vanilla.type("net.minecraft.world.level.Level").getDeclaredField("registryAccess");
        registryField.setAccessible(true);
        registryField.set(level, Vanilla.get("net.minecraft.core.RegistryAccess", "EMPTY"));
        return level;
    }

    private static Object context(Object random, Object access) throws Exception {
        var constructor = Vanilla.constructor("net.minecraft.util.context.ContextMap", "java.util.Map");
        var map = Vanilla.create(constructor, Map.of());
        var params = Vanilla.create(
            Vanilla.constructor(
                "net.minecraft.world.level.storage.loot.LootParams",
                "net.minecraft.server.level.ServerLevel",
                "net.minecraft.util.context.ContextMap",
                "java.util.Map",
                "float"),
            emptyLevel(),
            map,
            Map.of(),
            0f);
        return Vanilla.create(
            Vanilla.constructor(
                "net.minecraft.world.level.storage.loot.LootContext",
                "net.minecraft.world.level.storage.loot.LootParams",
                "net.minecraft.util.RandomSource",
                "net.minecraft.core.HolderGetter$Provider"),
            params,
            random,
            access);
    }
}
