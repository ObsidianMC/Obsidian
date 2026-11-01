import com.google.gson.*;
import com.mojang.serialization.*;
import io.netty.buffer.*;
import java.nio.file.*;
import java.util.*;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.Registry;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.protocol.game.ClientboundRecipeBookAddPacket;
import net.minecraft.resources.RegistryDataLoader;
import net.minecraft.resources.RegistryOps;
import net.minecraft.server.packs.PackType;
import net.minecraft.server.packs.repository.ServerPacksSource;
import net.minecraft.server.packs.resources.MultiPackResourceManager;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.crafting.*;
import net.minecraft.world.item.crafting.display.*;

/**
 * Captures 26.1's configurable special crafting recipes (crafting_dye, crafting_imbue and crafting_transmute):
 * - "crafts": for a table of crafting grids, the vanilla crafting recipe that matches first and what it assembles;
 * - "book": the recipe book entries vanilla's recipe manager makes of a few recipes of those types;
 * - "recipes": recipes of those types a data pack could add, whether vanilla loads each, and what grids craft with it
 *   alone.
 * Usage: generated vanilla data directory (containing minecraft/recipe), output JSON.
 * Use Java 25's source launcher with the vanilla jar and libraries on the classpath, like the other fixtures.
 */
public final class SpecialRecipeFixtures {
    private static final String[] DYES = {
        "white", "orange", "magenta", "light_blue", "yellow", "lime", "pink", "gray",
        "light_gray", "cyan", "purple", "blue", "brown", "green", "red", "black"
    };

    // Book entries are captured from recipes with item ingredients rather than tags: Obsidian flattens a tag into
    // its items when it loads a recipe, so it can only send a tag's items.
    private static final String[] BOOK_RECIPES = {
        """
        {"type": "minecraft:crafting_dye", "group": "dyed_armor", "target": "minecraft:leather_helmet",
         "dye": ["minecraft:red_dye", "minecraft:blue_dye"], "result": {"id": "minecraft:leather_helmet"}}
        """,
        """
        {"type": "minecraft:crafting_imbue", "source": "minecraft:lingering_potion", "material": "minecraft:arrow",
         "result": {"id": "minecraft:tipped_arrow", "count": 8}}
        """,
        """
        {"type": "minecraft:crafting_transmute", "category": "equipment", "group": "bundle_dye",
         "input": ["minecraft:bundle", "minecraft:red_bundle"], "material": "minecraft:blue_dye",
         "result": {"id": "minecraft:blue_bundle"}}
        """,
        """
        {"type": "minecraft:crafting_transmute", "group": "map_cloning", "input": "minecraft:filled_map",
         "material": "minecraft:map", "material_count": {"min": 1, "max": 8}, "add_material_count_to_result": true,
         "result": {}}
        """,
        """
        {"type": "minecraft:crafting_transmute", "input": ["minecraft:shulker_box", "minecraft:red_shulker_box"],
         "material": "minecraft:paper", "material_count": {"min": 2, "max": 3}, "result": {"count": 2}}
        """,
        """
        {"type": "minecraft:crafting_transmute", "input": ["minecraft:shulker_box", "minecraft:red_shulker_box"],
         "material": "minecraft:paper", "result": {"components": {"minecraft:custom_name": "Copy"}}}
        """,
        """
        {"type": "minecraft:crafting_transmute", "input": "minecraft:shulker_box", "material": "minecraft:blue_dye",
         "result": {"id": "minecraft:blue_shulker_box",
                    "components": {"minecraft:custom_name": "Blue box", "!minecraft:lore": {}}}}
        """,
        """
        {"type": "minecraft:crafting_imbue", "source": "minecraft:lingering_potion", "material": "minecraft:arrow",
         "result": {"id": "minecraft:tipped_arrow", "count": 8, "components": {"minecraft:custom_name": "Sharp"}}}
        """
    };

    private static DynamicOps<JsonElement> ops;
    private static RegistryAccess access;

    public static void main(String[] arguments) throws Exception {
        VanillaDumper.bootstrap();

        access = loadRegistries();
        ops = RegistryOps.create(JsonOps.INSTANCE, access);

        var recipes = readCraftingRecipes(Path.of(arguments[0], "minecraft", "recipe"));

        var crafts = new JsonArray();
        for (var grid : grids())
            crafts.add(craft(recipes, grid));

        var book = new JsonArray();
        for (var json : BOOK_RECIPES)
            book.add(bookEntries(json));

        var packs = new JsonArray();
        for (var recipe : packRecipes())
            packs.add(packRecipe(recipe));

        var output = new JsonObject();
        output.add("crafts", crafts);
        output.add("book", book);
        output.add("recipes", packs);
        var gson = new GsonBuilder().setPrettyPrinting().serializeNulls().create();
        Files.writeString(Path.of(arguments[1]), gson.toJson(output));
    }

    // The built-in registries, whose tags bootstrap bound (recipes name item tags), and the world registries from
    // vanilla's data pack: the grids' armour holds enchantments and trims.
    private static RegistryAccess loadRegistries() {
        var builtIn = new ArrayList<Registry<?>>();
        BuiltInRegistries.REGISTRY.forEach(builtIn::add);

        var pack = ServerPacksSource.createVanillaPackSource().fullResources();
        var resources = new MultiPackResourceManager(PackType.SERVER_DATA, List.of(pack));
        var lookups = new ArrayList<HolderLookup.RegistryLookup<?>>(builtIn);
        var world = RegistryDataLoader.load(resources, lookups, RegistryDataLoader.WORLD_REGISTRIES, Runnable::run)
            .join();

        var all = new ArrayList<Registry<?>>(builtIn);
        world.registries().forEach(entry -> all.add(entry.value()));
        return new RegistryAccess.ImmutableRegistryAccess(all);
    }

    /** A crafting grid of the table: its name, width (2 or 3) and slots, row by row; null slots are empty. */
    private record Grid(String name, int width, List<JsonObject> slots) {
    }

    /** A recipe a data pack could add, and the grids to craft with it. */
    private record PackRecipe(String json, List<Grid> grids) {
    }

    private static List<Grid> grids() {
        var grids = new ArrayList<Grid>();

        // Dyeing armour.
        grids.add(grid("helmet and red dye", 2, item("leather_helmet"), item("red_dye"), null, null));
        for (var dye : DYES)
            grids.add(grid("chestplate and " + dye + " dye", 3, item("leather_chestplate"), null, null, null,
                item(dye + "_dye"), null, null, null, null));
        grids.add(grid("blue boots and yellow dye", 2,
            item("leather_boots", "{\"minecraft:dyed_color\": 3949738}"), item("yellow_dye"), null, null));
        grids.add(grid("horse armour and three dyes", 2,
            item("red_dye"), item("leather_horse_armor"), item("red_dye"), item("white_dye")));
        grids.add(grid("wolf armour and eight dyes", 3,
            item("lime_dye"), item("cyan_dye"), item("black_dye"), item("pink_dye"), item("wolf_armor"),
            item("orange_dye"), item("lime_dye"), item("brown_dye"), item("gray_dye")));
        grids.add(grid("named helmet and green dye", 2,
            item("leather_helmet", "{\"minecraft:custom_name\": \"Hat\"}"), null, null, item("green_dye")));
        grids.add(grid("helmet and a stack of dye", 2, item("leather_helmet"), stack("red_dye", 5), null, null));
        grids.add(grid("helmet alone", 2, item("leather_helmet"), null, null, null));
        grids.add(grid("two helmets and dye", 3, item("leather_helmet"), item("leather_helmet"), item("red_dye"),
            null, null, null, null, null, null));
        grids.add(grid("helmet, dye and stick", 2, item("leather_helmet"), item("red_dye"), item("stick"), null));
        grids.add(grid("red and blue dye", 2, item("red_dye"), item("blue_dye"), null, null));

        // Tipping arrows.
        var arrow = item("arrow");
        var swiftness = item("lingering_potion",
            "{\"minecraft:potion_contents\": {\"potion\": \"minecraft:swiftness\"}}");
        var poison = item("lingering_potion",
            "{\"minecraft:potion_contents\": {\"potion\": \"minecraft:long_poison\", \"custom_color\": 1193046}}");
        grids.add(grid("arrows around a lingering potion of swiftness", 3,
            arrow, arrow, arrow, arrow, swiftness, arrow, arrow, arrow, arrow));
        grids.add(grid("arrows around a coloured lingering potion of poison", 3,
            arrow, arrow, arrow, arrow, poison, arrow, arrow, arrow, arrow));
        grids.add(grid("arrows around a plain lingering potion", 3,
            arrow, arrow, arrow, arrow, item("lingering_potion"), arrow, arrow, arrow, arrow));
        grids.add(grid("arrows around a splash potion", 3,
            arrow, arrow, arrow, arrow, item("splash_potion"), arrow, arrow, arrow, arrow));
        grids.add(grid("lingering potion in a corner", 3,
            swiftness, arrow, arrow, arrow, arrow, arrow, arrow, arrow, arrow));
        grids.add(grid("seven arrows around a lingering potion", 3,
            arrow, arrow, arrow, arrow, swiftness, arrow, arrow, arrow, null));

        // Transmuting: dyeing shulker boxes and bundles, and cloning maps.
        grids.add(grid("shulker box and red dye", 2, null, item("shulker_box"), item("red_dye"), null));
        grids.add(grid("named red shulker box with loot and blue dye", 3,
            item("red_shulker_box", "{\"minecraft:custom_name\": \"Loot\", \"minecraft:container\": "
                + "[{\"slot\": 0, \"item\": {\"id\": \"minecraft:diamond\", \"count\": 3}}]}"),
            null, null, null, null, null, null, null, item("blue_dye")));
        grids.add(grid("red shulker box and red dye", 2, item("red_shulker_box"), item("red_dye"), null, null));
        grids.add(grid("shulker box and two dyes", 2, item("shulker_box"), item("red_dye"), item("red_dye"), null));
        grids.add(grid("bundle of stone and lime dye", 2,
            item("bundle", "{\"minecraft:bundle_contents\": [{\"id\": \"minecraft:stone\", \"count\": 5}]}"),
            item("lime_dye"), null, null));
        var map = item("filled_map", "{\"minecraft:map_id\": 7}");
        var blank = item("map");
        grids.add(grid("map and a blank map", 2, map, blank, null, null));
        grids.add(grid("map and eight blank maps", 3, blank, blank, blank, blank, map, blank, blank, blank, blank));
        grids.add(grid("map and three stacked blank maps", 2, map, stack("map", 3), null, null));
        grids.add(grid("two maps", 2, map, item("filled_map", "{\"minecraft:map_id\": 8}"), null, null));
        grids.add(grid("blank maps alone", 2, blank, blank, null, null));
        grids.add(grid("map alone", 2, map, null, null, null));

        // Results that aren't valid stacks (ItemStack.validateStrict): a recipe that assembles one crafts nothing.
        grids.add(grid("map that doesn't stack and a blank map", 2,
            item("filled_map", "{\"minecraft:map_id\": 7, \"minecraft:max_stack_size\": 1}"), blank, null, null));
        grids.add(grid("map that stacks to two and two blank maps", 2,
            item("filled_map", "{\"minecraft:map_id\": 7, \"minecraft:max_stack_size\": 2}"), blank, blank, null));
        grids.add(grid("map without a stack size and a blank map", 2,
            item("filled_map", "{\"minecraft:map_id\": 7, \"!minecraft:max_stack_size\": {}}"), blank, null, null));
        grids.add(grid("damageable map and a blank map", 2,
            item("filled_map", "{\"minecraft:map_id\": 7, \"minecraft:max_damage\": 10}"), blank, null, null));
        grids.add(grid("stacking helmet and red dye", 2,
            item("leather_helmet", "{\"minecraft:max_stack_size\": 2}"), item("red_dye"), null, null));
        grids.add(grid("stacking damageable red shulker box and blue dye", 2,
            item("red_shulker_box", "{\"minecraft:max_stack_size\": 2, \"minecraft:max_damage\": 5}"),
            item("blue_dye"), null, null));

        // Components removed from the input, which the result keeps removed.
        grids.add(grid("red shulker box without lore and blue dye", 2,
            item("red_shulker_box", "{\"!minecraft:lore\": {}, \"!minecraft:rarity\": {}}"), item("blue_dye"),
            null, null));
        grids.add(grid("map without lore and a blank map", 2,
            item("filled_map", "{\"minecraft:map_id\": 7, \"!minecraft:lore\": {}}"), blank, null, null));
        grids.add(grid("helmet without attribute modifiers and red dye", 2,
            item("leather_helmet", "{\"!minecraft:attribute_modifiers\": {}}"), item("red_dye"), null, null));

        // Dyes whose dye component is overridden or removed: dye recipes take any dye color, transmute recipes
        // their material item whatever its color.
        var blueRedDye = item("red_dye", "{\"minecraft:dye\": \"blue\"}");
        var colorlessRedDye = item("red_dye", "{\"!minecraft:dye\": {}}");
        grids.add(grid("helmet and red dye that dyes blue", 2, item("leather_helmet"), blueRedDye, null, null));
        grids.add(grid("helmet and red dye without a color", 2, item("leather_helmet"), colorlessRedDye, null, null));
        grids.add(grid("helmet, red dye without a color and blue dye", 2,
            item("leather_helmet"), colorlessRedDye, item("blue_dye"), null));
        grids.add(grid("shulker box and red dye that dyes blue", 2, item("shulker_box"), blueRedDye, null, null));
        grids.add(grid("shulker box and red dye without a color", 2,
            item("shulker_box"), colorlessRedDye, null, null));

        // Armour with trims and enchantments, which dyeing keeps.
        grids.add(grid("trimmed chestplate and yellow dye", 2,
            item("leather_chestplate",
                "{\"minecraft:trim\": {\"material\": \"minecraft:gold\", \"pattern\": \"minecraft:coast\"}}"),
            item("yellow_dye"), null, null));
        grids.add(grid("enchanted red boots and blue dye", 2,
            item("leather_boots", "{\"minecraft:enchantments\": {\"minecraft:protection\": 2}, "
                + "\"minecraft:dyed_color\": 11546150}"),
            item("blue_dye"), null, null));

        // Potions with custom effects, colors and names, which tipped arrows take.
        var brew = item("lingering_potion", "{\"minecraft:potion_contents\": {\"potion\": \"minecraft:swiftness\", "
            + "\"custom_color\": 1193046, \"custom_name\": \"brew\", \"custom_effects\": "
            + "[{\"id\": \"minecraft:strength\", \"amplifier\": 1, \"duration\": 200}]}}");
        var effects = item("lingering_potion", "{\"minecraft:potion_contents\": {\"custom_effects\": "
            + "[{\"id\": \"minecraft:poison\", \"duration\": 100, \"show_particles\": false}]}}");
        var emptied = item("lingering_potion", "{\"!minecraft:potion_contents\": {}}");
        grids.add(grid("arrows around a lingering potion with custom effects", 3,
            arrow, arrow, arrow, arrow, brew, arrow, arrow, arrow, arrow));
        grids.add(grid("arrows around a lingering potion of only custom effects", 3,
            arrow, arrow, arrow, arrow, effects, arrow, arrow, arrow, arrow));
        grids.add(grid("arrows around a lingering potion without contents", 3,
            arrow, arrow, arrow, arrow, emptied, arrow, arrow, arrow, arrow));

        return grids;
    }

    private static List<PackRecipe> packRecipes() {
        var recipes = new ArrayList<PackRecipe>();
        var shulkerAndDye = grid("shulker box and blue dye", 2, item("shulker_box"), item("blue_dye"), null, null);
        var helmetAndDye = grid("helmet and red dye", 2, item("leather_helmet"), item("red_dye"), null, null);

        // Result counts: vanilla's result codecs take 1 to 99, whatever the item stacks to.
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_transmute", "input": "minecraft:shulker_box", "material": "minecraft:blue_dye",
             "result": {"id": "minecraft:blue_shulker_box", "count": 0}}
            """, List.of()));
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_transmute", "input": "minecraft:shulker_box", "material": "minecraft:blue_dye",
             "result": {"id": "minecraft:blue_shulker_box", "count": 100}}
            """, List.of()));
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_transmute", "input": "minecraft:shulker_box", "material": "minecraft:blue_dye",
             "result": {"id": "minecraft:blue_shulker_box", "count": 99}}
            """, List.of(shulkerAndDye)));
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_dye", "target": "minecraft:leather_helmet", "dye": "minecraft:red_dye",
             "result": {"id": "minecraft:leather_helmet", "count": 2}}
            """, List.of(helmetAndDye)));
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_imbue", "source": "minecraft:lingering_potion", "material": "minecraft:arrow",
             "result": {"id": "minecraft:tipped_arrow", "count": 100}}
            """, List.of()));

        // Result component patches, which apply over the input's components.
        var lootBoxAndDye = grid("named shulker box with loot and blue dye", 2,
            item("shulker_box", "{\"minecraft:custom_name\": \"Loot\", \"minecraft:container\": "
                + "[{\"slot\": 0, \"item\": {\"id\": \"minecraft:diamond\", \"count\": 3}}]}"),
            item("blue_dye"), null, null);
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_transmute", "input": "minecraft:shulker_box", "material": "minecraft:blue_dye",
             "result": {"id": "minecraft:blue_shulker_box", "components": {"minecraft:custom_name": "Blue box"}}}
            """, List.of(shulkerAndDye, lootBoxAndDye)));
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_transmute", "input": "minecraft:shulker_box", "material": "minecraft:blue_dye",
             "result": {"id": "minecraft:blue_shulker_box", "components": {}}}
            """, List.of(shulkerAndDye)));
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_transmute", "input": "minecraft:filled_map", "material": "minecraft:map",
             "material_count": {"min": 1, "max": 8}, "add_material_count_to_result": true,
             "result": {"components": {"minecraft:custom_name": "Copy", "!minecraft:lore": {}}}}
            """, List.of(
            grid("map with lore and a blank map", 2,
                item("filled_map", "{\"minecraft:map_id\": 7, \"minecraft:lore\": [\"Old\"]}"), item("map"),
                null, null))));
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_dye", "target": "minecraft:leather_helmet", "dye": "minecraft:red_dye",
             "result": {"id": "minecraft:leather_helmet",
                        "components": {"minecraft:custom_name": "Red hat", "minecraft:dyed_color": 255,
                                       "!minecraft:lore": {}}}}
            """, List.of(
            helmetAndDye,
            grid("blue helmet with lore and red dye", 2,
                item("leather_helmet", "{\"minecraft:dyed_color\": 3949738, \"minecraft:lore\": [\"Old\"]}"),
                item("red_dye"), null, null))));
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_dye", "target": "minecraft:leather_helmet", "dye": "minecraft:red_dye",
             "result": "minecraft:leather_helmet"}
            """, List.of(helmetAndDye)));
        var arrow = item("arrow");
        var swiftness = item("lingering_potion",
            "{\"minecraft:potion_contents\": {\"potion\": \"minecraft:swiftness\"}}");
        recipes.add(new PackRecipe("""
            {"type": "minecraft:crafting_imbue", "source": "minecraft:lingering_potion", "material": "minecraft:arrow",
             "result": {"id": "minecraft:tipped_arrow", "count": 8,
                        "components": {"minecraft:custom_name": "Sharp",
                                       "minecraft:potion_contents": {"potion": "minecraft:healing"}}}}
            """, List.of(
            grid("arrows around a lingering potion of swiftness", 3,
                arrow, arrow, arrow, arrow, swiftness, arrow, arrow, arrow, arrow),
            grid("arrows around a plain lingering potion", 3,
                arrow, arrow, arrow, arrow, item("lingering_potion"), arrow, arrow, arrow, arrow))));

        return recipes;
    }

    private static Grid grid(String name, int width, JsonObject... slots) {
        return new Grid(name, width, Arrays.asList(slots));
    }

    private static JsonObject item(String name) {
        return stack(name, 1);
    }

    private static JsonObject stack(String name, int count) {
        var json = new JsonObject();
        json.addProperty("id", "minecraft:" + name);
        json.addProperty("count", count);

        return json;
    }

    private static JsonObject item(String name, String components) {
        var json = item(name);
        json.add("components", JsonParser.parseString(components));

        return json;
    }

    // Decodes every crafting recipe vanilla evaluates by its own rules, in identifier order like vanilla's recipe
    // manager tries them. The older special recipes (CustomRecipe: firework stars, banner duplicates, ...) are left
    // out: Obsidian doesn't evaluate them yet (docs/known-gaps/special-crafting-recipes.md),
    // and some need a level to match.
    private static Map<String, CraftingRecipe> readCraftingRecipes(Path directory) throws Exception {
        List<Path> files;
        // Crafting recipes are at the top; brewing recipes have a directory of their own.
        try (var listing = Files.list(directory)) {
            files = listing.filter(Files::isRegularFile).sorted().toList();
        }

        var recipes = new LinkedHashMap<String, CraftingRecipe>();
        for (var file : files) {
            var json = JsonParser.parseString(Files.readString(file));
            var type = json.getAsJsonObject().get("type").getAsString();
            if (!type.startsWith("minecraft:crafting_"))
                continue;

            var recipe = Recipe.DIRECT_CODEC.parse(ops, json).getOrThrow();
            if (!(recipe instanceof CraftingRecipe crafting) || recipe instanceof CustomRecipe)
                continue;

            var name = file.getFileName().toString().replace(".json", "");
            recipes.put("minecraft:" + name, crafting);
        }

        return recipes;
    }

    private static JsonObject craft(Map<String, CraftingRecipe> recipes, Grid grid) {
        var stacks = new ArrayList<ItemStack>();
        var slots = new JsonArray();
        for (var slot : grid.slots()) {
            var stack = slot == null ? ItemStack.EMPTY : ItemStack.CODEC.parse(ops, slot).getOrThrow();
            stacks.add(stack);
            slots.add(slot == null ? JsonNull.INSTANCE : new JsonPrimitive(encode(stack)));
        }

        var row = new JsonObject();
        row.addProperty("name", grid.name());
        row.addProperty("width", grid.width());
        row.add("grid", slots);

        var input = CraftingInput.of(grid.width(), grid.width(), stacks);
        for (var recipe : recipes.entrySet()) {
            if (!recipe.getValue().matches(input, null))
                continue;

            row.addProperty("recipe", recipe.getKey());
            row.addProperty("result", encode(recipe.getValue().assemble(input)));
            return row;
        }

        row.add("recipe", JsonNull.INSTANCE);
        row.add("result", JsonNull.INSTANCE);
        return row;
    }

    private static JsonObject packRecipe(PackRecipe pack) {
        var json = JsonParser.parseString(pack.json());
        var parsed = Recipe.DIRECT_CODEC.parse(ops, json);

        var crafts = new JsonArray();
        if (parsed.result().orElse(null) instanceof CraftingRecipe crafting) {
            var recipes = Map.of("probe:recipe", crafting);
            for (var grid : pack.grids())
                crafts.add(craft(recipes, grid));
        }

        var row = new JsonObject();
        row.add("recipe", json);
        row.addProperty("loads", parsed.isSuccess());
        row.add("crafts", crafts);
        return row;
    }

    // The recipe's book entries, like vanilla's RecipeManager.unpackRecipeInfo makes them: one per display, numbered
    // from 0 here, in group 0 when the recipe has a group.
    private static JsonObject bookEntries(String json) {
        var recipe = (CraftingRecipe) Recipe.DIRECT_CODEC.parse(ops, JsonParser.parseString(json)).getOrThrow();
        var group = recipe.group().isEmpty() ? OptionalInt.empty() : OptionalInt.of(0);
        var requirements = recipe.isSpecial()
            ? Optional.<List<Ingredient>>empty()
            : Optional.of(recipe.placementInfo().ingredients());

        var entries = new ArrayList<ClientboundRecipeBookAddPacket.Entry>();
        for (var display : recipe.display()) {
            var id = new RecipeDisplayId(entries.size());
            var entry = new RecipeDisplayEntry(id, display, group, recipe.recipeBookCategory(), requirements);
            entries.add(new ClientboundRecipeBookAddPacket.Entry(entry, (byte) 0));
        }

        var row = new JsonObject();
        row.add("recipe", JsonParser.parseString(json));
        row.addProperty("packet", encodePacket(new ClientboundRecipeBookAddPacket(entries, false)));
        return row;
    }

    private static String encode(ItemStack stack) {
        var bytes = Unpooled.buffer();
        try {
            ItemStack.OPTIONAL_STREAM_CODEC.encode(new RegistryFriendlyByteBuf(bytes, access), stack);
            return ByteBufUtil.hexDump(bytes);
        } finally {
            bytes.release();
        }
    }

    private static String encodePacket(ClientboundRecipeBookAddPacket packet) {
        var bytes = Unpooled.buffer();
        try {
            ClientboundRecipeBookAddPacket.STREAM_CODEC.encode(new RegistryFriendlyByteBuf(bytes, access), packet);
            return ByteBufUtil.hexDump(bytes);
        } finally {
            bytes.release();
        }
    }
}
