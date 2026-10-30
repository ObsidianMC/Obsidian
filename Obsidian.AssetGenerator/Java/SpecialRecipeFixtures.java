import com.google.gson.*;
import com.mojang.serialization.*;
import io.netty.buffer.*;
import java.nio.file.*;
import java.util.*;
import net.minecraft.core.RegistryAccess;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.protocol.game.ClientboundRecipeBookAddPacket;
import net.minecraft.resources.RegistryOps;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.crafting.*;
import net.minecraft.world.item.crafting.display.*;

/**
 * Captures 26.1's configurable special crafting recipes (crafting_dye, crafting_imbue and crafting_transmute):
 * - "crafts": for a table of crafting grids, the vanilla crafting recipe that matches first and what it assembles;
 * - "book": the recipe book entries vanilla's recipe manager makes of a few recipes of those types.
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
        """
    };

    private static DynamicOps<JsonElement> ops;
    private static RegistryAccess access;

    public static void main(String[] arguments) throws Exception {
        VanillaDumper.bootstrap();

        // The built-in registries, whose tags bootstrap bound: recipes name item tags, and the grids' stacks only
        // refer to built-in registries.
        access = RegistryAccess.fromRegistryOfRegistries(BuiltInRegistries.REGISTRY);
        ops = RegistryOps.create(JsonOps.INSTANCE, access);

        var recipes = readCraftingRecipes(Path.of(arguments[0], "minecraft", "recipe"));

        var crafts = new JsonArray();
        for (var grid : grids())
            crafts.add(craft(recipes, grid));

        var book = new JsonArray();
        for (var json : BOOK_RECIPES)
            book.add(bookEntries(json));

        var output = new JsonObject();
        output.add("crafts", crafts);
        output.add("book", book);
        var gson = new GsonBuilder().setPrettyPrinting().serializeNulls().create();
        Files.writeString(Path.of(arguments[1]), gson.toJson(output));
    }

    /** A crafting grid of the table: its name, width (2 or 3) and slots, row by row; null slots are empty. */
    private record Grid(String name, int width, List<JsonObject> slots) {
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

        return grids;
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
    // out: Obsidian doesn't evaluate them yet (docs/known-gaps.md), and some need a level to match.
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
