import com.google.gson.*;
import com.mojang.serialization.*;
import java.nio.file.*;
import java.util.*;

/**
 * Captures what vanilla's brewing recipes brew: for every bottle (potion, splash and lingering potion), every potion
 * and every item some brewing recipe takes as its reagent (and one item none takes), the item vanilla's first
 * matching recipe assembles, or nothing.
 * Usage: generated vanilla data directory (containing minecraft/recipe/brewing), output JSON.
 * Use Java 25's source launcher with the vanilla jar and libraries on the classpath, like the other fixtures.
 */
public final class BrewingFixtures {
    private static final String STACK = "net.minecraft.world.item.ItemStack";
    private static final String RECIPE = "net.minecraft.world.item.crafting.Recipe";
    private static final String INPUT = "net.minecraft.world.item.crafting.BrewingInput";
    private static final String[] BOTTLES = {
        "minecraft:potion",
        "minecraft:splash_potion",
        "minecraft:lingering_potion"
    };

    // An item no vanilla brewing recipe takes, so the capture has rows where nothing brews for a reason other than the
    // bottle's potion.
    private static final String NOT_A_REAGENT = "minecraft:dirt";

    private static DynamicOps<JsonElement> ops;

    public static void main(String[] arguments) throws Exception {
        VanillaDumper.bootstrap();
        var access = VanillaDumper.vanillaRegistries();
        ops = createOps(access);

        var recipes = new ArrayList<Object>();
        var reagents = new TreeSet<String>();
        reagents.add(NOT_A_REAGENT);
        readRecipes(Path.of(arguments[0], "minecraft", "recipe", "brewing"), recipes, reagents);

        var potions = potionNames();
        var bottleConstructor = Vanilla.constructor(INPUT, STACK, STACK);
        var brewingMatches = Vanilla.method("net.minecraft.world.item.crafting.BrewingRecipe", "matches", INPUT);
        var assemble = Vanilla.method("net.minecraft.world.item.crafting.BrewingRecipe", "assemble", INPUT);

        var rows = new JsonArray();
        for (var bottle : BOTTLES) {
            for (var potion : potions) {
                for (var reagent : reagents) {
                    var input = Vanilla.create(bottleConstructor, potionStack(bottle, potion), stack(reagent));
                    var result = brew(recipes, input, brewingMatches, assemble);
                    if (result == null)
                        continue;

                    var row = new JsonObject();
                    row.addProperty("bottle", bottle);
                    row.addProperty("potion", potion);
                    row.addProperty("reagent", reagent);
                    row.add("result", encode(result));
                    rows.add(row);
                }
            }
        }

        var output = new JsonObject();
        output.add("bottles", toArray(Arrays.asList(BOTTLES)));
        output.add("potions", toArray(potions));
        output.add("reagents", toArray(reagents));
        output.add("brews", rows);
        Files.writeString(Path.of(arguments[1]), new GsonBuilder().setPrettyPrinting().create().toJson(output));
    }

    @SuppressWarnings("unchecked")
    private static DynamicOps<JsonElement> createOps(Object access) {
        var create = Vanilla.method(
            "net.minecraft.resources.RegistryOps",
            "create",
            "com.mojang.serialization.DynamicOps",
            "net.minecraft.core.HolderLookup$Provider");

        return (DynamicOps<JsonElement>) Vanilla.call(create, null, JsonOps.INSTANCE, access);
    }

    // Decodes every brewing recipe, in identifier order like vanilla's recipe manager, and collects their reagents.
    private static void readRecipes(Path directory, List<Object> recipes, Set<String> reagents) throws Exception {
        List<Path> files;
        try (var listing = Files.list(directory)) {
            files = listing.sorted().toList();
        }

        for (var file : files) {
            var json = JsonParser.parseString(Files.readString(file));
            recipes.add(unwrap(decode(RECIPE, json)));

            var reagent = json.getAsJsonObject().getAsJsonObject("reagent").get("item");
            if (reagent.isJsonArray()) {
                for (var item : reagent.getAsJsonArray())
                    reagents.add(item.getAsString());
            } else {
                reagents.add(reagent.getAsString());
            }
        }
    }

    // The recipe codec decodes to a holder of the recipe.
    private static Object unwrap(Object recipe) {
        if (!Vanilla.type("net.minecraft.core.Holder").isInstance(recipe))
            return recipe;

        return Vanilla.call(Vanilla.method("net.minecraft.core.Holder", "value"), recipe);
    }

    // The first recipe that matches, like vanilla's RecipeManager.getRecipeFor, assembled.
    private static Object brew(List<Object> recipes, Object input, java.lang.reflect.Method matches,
        java.lang.reflect.Method assemble) {
        for (var recipe : recipes) {
            if ((boolean) Vanilla.call(matches, recipe, input))
                return Vanilla.call(assemble, recipe, input);
        }

        return null;
    }

    private static List<String> potionNames() {
        var registry = Vanilla.get("net.minecraft.core.registries.BuiltInRegistries", "POTION");
        var keys = (Set<?>) Vanilla.call(Vanilla.method("net.minecraft.core.Registry", "keySet"), registry);
        var names = new ArrayList<String>();
        for (var key : keys)
            names.add(key.toString());

        Collections.sort(names);
        return names;
    }

    private static Object potionStack(String bottle, String potion) {
        var json = new JsonObject();
        json.addProperty("id", bottle);

        var contents = new JsonObject();
        contents.addProperty("potion", potion);

        var components = new JsonObject();
        components.add("minecraft:potion_contents", contents);
        json.add("components", components);

        return decode(STACK, json);
    }

    private static Object stack(String item) {
        var json = new JsonObject();
        json.addProperty("id", item);

        return decode(STACK, json);
    }

    @SuppressWarnings("unchecked")
    private static Object decode(String owner, JsonElement json) {
        var codec = (Codec<Object>) Vanilla.get(owner, "CODEC");
        return codec.parse(ops, json).getOrThrow();
    }

    @SuppressWarnings("unchecked")
    private static JsonElement encode(Object stack) {
        var codec = (Codec<Object>) Vanilla.get(STACK, "CODEC");
        return codec.encodeStart(ops, stack).getOrThrow();
    }

    private static JsonArray toArray(Collection<String> values) {
        var array = new JsonArray();
        for (var value : values)
            array.add(value);

        return array;
    }
}
