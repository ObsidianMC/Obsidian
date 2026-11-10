import com.google.gson.GsonBuilder;
import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;
import com.mojang.serialization.JsonOps;
import java.io.InputStreamReader;
import java.nio.charset.StandardCharsets;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.Collection;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.function.Predicate;
import net.minecraft.advancements.CriterionTriggerInstance;
import net.minecraft.advancements.triggers.CriterionTrigger;
import net.minecraft.core.BlockPos;
import net.minecraft.core.HolderLookup;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.core.registries.Registries;
import net.minecraft.resources.Identifier;
import net.minecraft.resources.RegistryOps;
import net.minecraft.resources.ResourceKey;
import net.minecraft.server.MinecraftServer;
import net.minecraft.server.PlayerAdvancements;
import net.minecraft.server.PlayerAdvancements.TriggerInstanceKey;
import net.minecraft.server.dedicated.DedicatedServer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.stats.ServerRecipeBook;
import net.minecraft.util.RandomSource;
import net.minecraft.util.debug.ServerDebugSubscribers;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.ExperienceOrb;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.flag.FeatureFlagSet;
import net.minecraft.world.flag.FeatureFlags;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.crafting.Recipe;
import net.minecraft.world.item.crafting.RecipeHolder;
import net.minecraft.world.item.crafting.RecipeManager;
import net.minecraft.world.level.entity.EntityTypeTest;
import net.minecraft.world.level.gamerules.GameRules;
import net.minecraft.world.level.block.Blocks;
import net.minecraft.world.level.block.entity.AbstractFurnaceBlockEntity;
import net.minecraft.world.level.block.entity.BlastFurnaceBlockEntity;
import net.minecraft.world.level.block.entity.FurnaceBlockEntity;
import net.minecraft.world.level.block.entity.SmokerBlockEntity;
import net.minecraft.world.level.levelgen.PositionalRandomFactory;
import net.minecraft.world.phys.AABB;
import net.minecraft.world.phys.Vec3;

/**
 * Takes a furnace's, blast furnace's or smoker's result, or breaks it, through vanilla's own routines: a player taking
 * the result runs AbstractFurnaceBlockEntity.awardUsedRecipesAndPopExperience (what the result slot runs), and breaking
 * runs its preRemoveSideEffects. The furnace's used recipes are set as cooking sets them, the player's recipe book holds
 * the case's known recipes, and the level's random records every draw.
 *
 * Usage: FurnaceFixtures <fixture.json>. Each case's name, seed, machine, used, known, limitedCrafting and action are
 * its input; awarded, unlocked, triggers, crafted, orbs, draws and usedAfter are replaced.
 */
public final class FurnaceFixtures {
    private static final String NOTES = "Vanilla 26.3 cooking machines giving out what they cooked, recorded by the "
        + "asset generator's FurnaceFixtures.java. Each case's machine has cooked its used recipes as many times as "
        + "each says (setRecipeUsed, as cooking calls it), then either a player takes its result (action take: "
        + "awardUsedRecipesAndPopExperience, which the result slot runs on a pickup or quick move) or it's broken "
        + "(action break: preRemoveSideEffects). The player's recipe book knows the case's known recipes, and the "
        + "limited_crafting rule is limitedCrafting. The level's random is a legacy random seeded with seed. "
        + "awarded lists the recipes the player was given, unlocked those the recipe book added (each firing "
        + "recipe_unlocked), triggers the criterion triggers fired in order, crafted the recipes recipe_crafted was "
        + "fired for, orbs the experience orbs' values, draws the level random's draws in order, and usedAfter how "
        + "many recipes the machine still holds as used. A machine iterates its used recipes in an identity hash "
        + "order, so awarded, unlocked, crafted and orbs are sorted, and cases with several recipes give at most one "
        + "of them a fractional experience, which keeps the draws in a fixed order.";

    public static void main(String[] arguments) throws Exception {
        var version = VanillaDumper.bootstrap();
        var registries = (HolderLookup.Provider) VanillaDumper.vanillaRegistries();
        var path = Path.of(arguments[0]);
        var root = JsonParser.parseString(Files.readString(path)).getAsJsonObject();
        root.addProperty("_notes", NOTES);
        root.addProperty("version", version);

        for (var entry : root.getAsJsonArray("cases")) {
            capture(entry.getAsJsonObject(), registries);
        }

        var gson = new GsonBuilder().serializeNulls().setPrettyPrinting().disableHtmlEscaping().create();
        Files.writeString(path, gson.toJson(root) + "\n");
    }

    private static void capture(JsonObject input, HolderLookup.Provider registries) throws Exception {
        for (var key : List.of("awarded", "unlocked", "triggers", "crafted", "orbs", "draws", "usedAfter")) {
            input.remove(key);
        }

        var recipes = new Recipes(registries);
        var random = new RecordingRandom(input.get("seed").getAsLong());
        var limitedCrafting = input.has("limitedCrafting") && input.get("limitedCrafting").getAsBoolean();
        var level = ProbeLevel.allocate(random, recipes, limitedCrafting);

        var furnace = machine(input.get("machine").getAsString());
        furnace.setLevel(level);
        for (var used : input.getAsJsonObject("used").entrySet()) {
            var recipe = recipes.holder(used.getKey());
            for (var time = 0; time < used.getValue().getAsInt(); time++) {
                furnace.setRecipeUsed(recipe);
            }
        }

        var player = ProbePlayer.allocate(level, recipes);
        for (var known : input.getAsJsonArray("known")) {
            player.book.add(recipes.holder(known.getAsString()).id());
        }

        switch (input.get("action").getAsString()) {
            case "take" -> furnace.awardUsedRecipesAndPopExperience(player);
            case "break" -> furnace.preRemoveSideEffects(furnace.getBlockPos(), furnace.getBlockState());
            default -> throw new IllegalArgumentException("Unknown action " + input.get("action"));
        }

        input.add("awarded", sorted(player.awarded));
        input.add("unlocked", sorted(player.unlocked));
        input.add("triggers", player.advancements().triggers);
        input.add("crafted", sorted(player.crafted));

        var orbs = new ArrayList<Integer>(level.orbs);
        orbs.sort(null);
        var orbValues = new JsonArray();
        orbs.forEach(orbValues::add);
        input.add("orbs", orbValues);

        input.add("draws", random.draws);
        input.addProperty("usedAfter", usedCount(furnace));
    }

    private static AbstractFurnaceBlockEntity machine(String name) {
        var pos = BlockPos.ZERO;
        return switch (name) {
            case "furnace" -> new FurnaceBlockEntity(pos, Blocks.FURNACE.defaultBlockState());
            case "blast_furnace" -> new BlastFurnaceBlockEntity(pos, Blocks.BLAST_FURNACE.defaultBlockState());
            case "smoker" -> new SmokerBlockEntity(pos, Blocks.SMOKER.defaultBlockState());
            default -> throw new IllegalArgumentException("Unknown machine " + name);
        };
    }

    // The number of recipes the machine holds as used, read from its private map.
    private static int usedCount(AbstractFurnaceBlockEntity furnace) throws Exception {
        var field = AbstractFurnaceBlockEntity.class.getDeclaredField("recipesUsed");
        field.setAccessible(true);
        return ((Map<?, ?>) field.get(furnace)).size();
    }

    private static JsonArray sorted(List<String> values) {
        var copy = new ArrayList<>(values);
        copy.sort(null);
        var result = new JsonArray();
        copy.forEach(result::add);
        return result;
    }

    private static <T> T allocateInstance(Class<T> type) throws Exception {
        var field = Class.forName("sun.misc.Unsafe").getDeclaredField("theUnsafe");
        field.setAccessible(true);
        var unsafe = field.get(null);
        return type.cast(unsafe.getClass().getMethod("allocateInstance", Class.class).invoke(unsafe, type));
    }

    private static void set(Class<?> owner, Object target, String name, Object value) throws Exception {
        var field = owner.getDeclaredField(name);
        field.setAccessible(true);
        field.set(target, value);
    }

    /**
     * Vanilla's recipes, decoded from the vanilla data pack's files (data/minecraft/recipe/<path>.json) as a server
     * loads them, and looked up by key as the server's recipe manager looks them up.
     */
    private static final class Recipes extends RecipeManager {
        private final HolderLookup.Provider registries;
        private final Map<ResourceKey<Recipe<?>>, RecipeHolder<?>> loaded = new HashMap<>();

        Recipes(HolderLookup.Provider registries) {
            super(registries);
            this.registries = registries;
        }

        RecipeHolder<?> holder(String id) throws Exception {
            var key = ResourceKey.create(Registries.RECIPE, Identifier.parse(id));
            var known = loaded.get(key);
            if (known != null) {
                return known;
            }

            var resource = "/data/" + key.identifier().getNamespace() + "/recipe/" + key.identifier().getPath()
                + ".json";
            try (var stream = FurnaceFixtures.class.getResourceAsStream(resource)) {
                if (stream == null) {
                    throw new IllegalStateException("No recipe " + resource);
                }

                var json = JsonParser.parseReader(new InputStreamReader(stream, StandardCharsets.UTF_8));
                var ops = RegistryOps.create(JsonOps.INSTANCE, registries);
                Recipe<?> recipe = Recipe.CODEC.parse(ops, json).getOrThrow().value();
                var holder = new RecipeHolder<Recipe<?>>(key, recipe);
                loaded.put(key, holder);
                return holder;
            }
        }

        @Override public Optional<RecipeHolder<?>> byKey(ResourceKey<Recipe<?>> recipeId) {
            return Optional.ofNullable(loaded.get(recipeId));
        }
    }

    /** A legacy (java.util.Random) source, as the level's, that records each draw and its result. */
    private static final class RecordingRandom implements RandomSource {
        private final RandomSource source;
        final JsonArray draws = new JsonArray();

        RecordingRandom(long seed) {
            this.source = RandomSource.create(seed);
        }

        private void record(String call, Integer bound, Object value) {
            var draw = new JsonObject();
            draw.addProperty("call", call);
            if (bound != null) {
                draw.addProperty("bound", bound);
            }

            if (value instanceof Boolean flag) {
                draw.addProperty("value", flag);
            } else {
                draw.addProperty("value", (Number) value);
            }

            draws.add(draw);
        }

        @Override public RandomSource fork() {
            throw new UnsupportedOperationException("fork");
        }

        @Override public PositionalRandomFactory forkPositional() {
            throw new UnsupportedOperationException("forkPositional");
        }

        @Override public void setSeed(long seed) {
            throw new UnsupportedOperationException("setSeed");
        }

        @Override public int nextInt() {
            var value = source.nextInt();
            record("nextInt", null, value);
            return value;
        }

        @Override public int nextInt(int bound) {
            var value = source.nextInt(bound);
            record("nextInt", bound, value);
            return value;
        }

        @Override public long nextLong() {
            var value = source.nextLong();
            record("nextLong", null, value);
            return value;
        }

        @Override public boolean nextBoolean() {
            var value = source.nextBoolean();
            record("nextBoolean", null, value);
            return value;
        }

        @Override public float nextFloat() {
            var value = source.nextFloat();
            record("nextFloat", null, value);
            return value;
        }

        @Override public double nextDouble() {
            var value = source.nextDouble();
            record("nextDouble", null, value);
            return value;
        }

        @Override public double nextGaussian() {
            var value = source.nextGaussian();
            record("nextGaussian", null, value);
            return value;
        }
    }

    /**
     * The level: its random records draws, its recipes are vanilla's, it's open air with no orbs to merge with, and
     * the orbs added are recorded rather than spawned.
     */
    private static final class ProbeLevel extends ServerLevel {
        RandomSource tickRandom;
        Recipes recipes;
        GameRules rules;
        MinecraftServer owner;
        List<Integer> orbs;

        private ProbeLevel() {
            super(null, null, null, null, null, null, false, 0, List.of(), false);
            throw new UnsupportedOperationException();
        }

        static ProbeLevel allocate(RandomSource random, Recipes recipes, boolean limitedCrafting) throws Exception {
            var level = allocateInstance(ProbeLevel.class);
            level.tickRandom = random;
            level.recipes = recipes;
            level.rules = new GameRules(List.of(GameRules.LIMITED_CRAFTING));
            level.rules.set(GameRules.LIMITED_CRAFTING, limitedCrafting, null);
            level.orbs = new ArrayList<>();

            level.owner = allocateInstance(DedicatedServer.class);
            set(MinecraftServer.class, level.owner, "debugSubscribers", new ServerDebugSubscribers(level.owner));
            return level;
        }

        @Override public RandomSource getRandom() {
            return tickRandom;
        }

        @Override public RecipeManager recipeAccess() {
            return recipes;
        }

        @Override public GameRules getGameRules() {
            return rules;
        }

        @Override public MinecraftServer getServer() {
            return owner;
        }

        @Override public FeatureFlagSet enabledFeatures() {
            return FeatureFlags.DEFAULT_FLAGS;
        }

        @Override public int getNextEntityId() {
            return orbs.size() + 1;
        }

        @Override public boolean isClientSide() {
            return false;
        }

        // The orbs appear in open air.
        @Override public boolean noCollision(Entity entity, AABB bounds, boolean alwaysCollideWithFluids) {
            return true;
        }

        // No orbs lie around to merge with.
        @Override public <T extends Entity> List<T> getEntities(EntityTypeTest<Entity, T> type, AABB bounds,
            Predicate<? super T> selector) {
            return List.of();
        }

        @Override public boolean addFreshEntity(Entity entity) {
            if (entity instanceof ExperienceOrb orb) {
                orbs.add(orb.getValue());
            }

            return true;
        }
    }

    /**
     * The player taking the result: its recipe book is vanilla's, with a display resolver that records the recipes
     * it adds, and its advancements record the triggers fired rather than matching them.
     */
    private static final class ProbePlayer extends ServerPlayer {
        ServerRecipeBook book;
        RecordingAdvancements tracker;
        List<String> awarded;
        List<String> unlocked;
        List<String> crafted;

        private ProbePlayer() {
            super(null, null, null, null);
            throw new UnsupportedOperationException();
        }

        static ProbePlayer allocate(ProbeLevel level, Recipes recipes) throws Exception {
            var player = allocateInstance(ProbePlayer.class);
            player.awarded = new ArrayList<>();
            player.unlocked = new ArrayList<>();
            player.crafted = new ArrayList<>();
            player.book = new ServerRecipeBook((id, output) -> player.unlocked.add(id.identifier().toString()));
            player.tracker = allocateInstance(RecordingAdvancements.class);
            player.tracker.triggers = new JsonArray();
            set(ServerPlayer.class, player, "recipeBook", player.book);
            set(Entity.class, player, "level", level);
            set(Entity.class, player, "position", new Vec3(0.5, 64, 0.5));
            return player;
        }

        RecordingAdvancements advancements() {
            return tracker;
        }

        @Override public PlayerAdvancements getAdvancements() {
            return tracker;
        }

        @Override public int awardRecipes(Collection<RecipeHolder<?>> recipes) {
            for (var recipe : recipes) {
                awarded.add(recipe.id().identifier().toString());
            }

            return super.awardRecipes(recipes);
        }

        @Override public void triggerRecipeCrafted(RecipeHolder<?> recipe, List<ItemStack> itemStacks) {
            crafted.add(recipe.id().identifier().toString());
            super.triggerRecipeCrafted(recipe, itemStacks);
        }
    }

    /** A player's advancements that only record which criterion triggers were fired. */
    private static final class RecordingAdvancements extends PlayerAdvancements {
        JsonArray triggers;

        private RecordingAdvancements() {
            super(null, null, null, null, null);
            throw new UnsupportedOperationException();
        }

        @Override public <T extends CriterionTriggerInstance> Map<TriggerInstanceKey, T> getTriggerMapForType(
            CriterionTrigger<T> type) {
            triggers.add(BuiltInRegistries.TRIGGER_TYPES.getKey(type).toString());
            return null;
        }
    }
}
