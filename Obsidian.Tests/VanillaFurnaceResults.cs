using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Entities;
using Obsidian.Registries;
using Obsidian.Tests.Fakes;
using Obsidian.Tests.Utilities;
using Obsidian.WorldData;
using Xunit;
using Xunit.Abstractions;

namespace Obsidian.Tests;

/// <summary>
/// Replays <c>furnace_results.json</c>: what vanilla's furnaces, blast furnaces and smokers give a player taking their
/// result, and drop when broken, from <c>Java/FurnaceFixtures.java</c> (<c>CaptureFixtures=furnace_results</c>).
/// </summary>
public sealed class VanillaFurnaceResults(ITestOutputHelper output)
{
    private static readonly JsonElement fixture = LoadFixture();

    private const string RecipeUnlocked = "minecraft:recipe_unlocked";

    /// <summary>
    /// Each case's machine holds its used recipes and gives them out with the case's seed drawing the fractions:
    /// taking the result through <see cref="FurnaceBlockEntity.TakeCookedRecipes"/> and the player's
    /// <c>AwardCookedRecipesAsync</c>, as a click on the result runs them, and breaking through
    /// <see cref="FurnaceBlockEntity.TakeExperience"/>, as removing the machine does. The recipes awarded, the orbs,
    /// what's left used and the recipes unlocked (by the <c>recipe_unlocked</c> criteria of vanilla's recipe
    /// advancements they meet) must be vanilla's; a broken machine's experience must add up to vanilla's.
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ReplayFurnaceResultReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        await using var context = TestServerFactory.CreateServer($"furnace-results-{Guid.NewGuid():N}");
        context.World.RecipeSource = () => context.Server.RecipeRegistry.Current;
        WorldBlockEntities.AddEmptyChunk(context.World);

        // Each case's machine on a layer of its own.
        var cases = fixture.GetProperty("cases").EnumerateArray()
            .Select((probe, index) => (Probe: probe, Index: index))
            .ToArray();
        await FixtureCases.AssertAllAsync(cases, item => Name(item.Probe), item => ReplayAsync(context, item.Probe,
            item.Index));

        output.WriteLine($"Replayed {cases.Length} furnace result cases.");
    }

    private static string Name(JsonElement probe) => probe.GetProperty("name").GetString()!;

    private static async Task ReplayAsync(RealServerContext context, JsonElement probe, int index)
    {
        var recipes = context.Server.RecipeRegistry.Current;
        var furnace = await PlaceMachineAsync(context.World, probe, new Vector(1, 64 + index, 1));
        foreach (var used in probe.GetProperty("used").EnumerateObject())
            furnace.RecipesUsed[RecipeKey(recipes, used.Name)] = used.Value.GetInt32();

        var random = new LegacyRandomSource(probe.GetProperty("seed").GetInt64());
        var differences = new List<string>();

        if (probe.GetProperty("action").GetString() == "break")
        {
            int experience;
            using (furnace.Inventory.Lock())
                experience = furnace.TakeExperience(recipes, random.NextFloat);

            var vanilla = probe.GetProperty("orbs").EnumerateArray().Sum(orb => orb.GetInt32());
            BlockProbes.Expect(differences, "experience", vanilla, experience);
        }
        else
        {
            await TakeAsync(differences, context, probe, index, furnace, random);
        }

        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    // A player who knows the case's known recipes takes the result: the machine gives out its recipes and their
    // experience, and the player is given the recipes.
    private static async Task TakeAsync(
        List<string> differences,
        RealServerContext context,
        JsonElement probe,
        int index,
        FurnaceBlockEntity furnace,
        LegacyRandomSource random)
    {
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, $"Cook{index}");
        foreach (var known in probe.GetProperty("known").EnumerateArray())
            await player.UnlockProgressRecipeAsync(known.GetString()!);

        var unlockedBefore = UnlockedCriteria(player);

        List<FurnaceBlockEntity.CookedRecipe> cooked;
        using (furnace.Inventory.Lock())
            cooked = furnace.TakeCookedRecipes(context.Server.RecipeRegistry.Current, random.NextFloat);

        await player.AwardCookedRecipesAsync(cooked.Select(recipe => recipe.Recipe.Identifier));

        var awarded = cooked.Select(recipe => AdvancementsRegistry.Qualify(recipe.Recipe.Identifier));
        Compare(differences, "awarded", probe.GetProperty("awarded"), awarded);

        var orbs = cooked.SelectMany(recipe => Orbs(recipe.Experience)).Select(orb => orb.ToString());
        Compare(differences, "orbs", probe.GetProperty("orbs"), orbs);

        BlockProbes.Expect(differences, "used after", probe.GetProperty("usedAfter").GetInt32(),
            furnace.RecipesUsed.Count);

        // The recipes newly unlocked, by the recipe_unlocked criteria they met. Obsidian's recipe book holds no cooking
        // displays, so the client isn't sent them (see docs/known-gaps/progress-parity.md).
        var criteria = UnlockedCriteria(player).Except(unlockedBefore);
        Compare(differences, "recipe_unlocked criteria met", probe.GetProperty("unlocked"), criteria);
    }

    private static async Task<FurnaceBlockEntity> PlaceMachineAsync(WorldData.World world, JsonElement probe,
        Vector position)
    {
        var machine = probe.GetProperty("machine").GetString() switch
        {
            "blast_furnace" => BlocksRegistry.BlastFurnace,
            "smoker" => BlocksRegistry.Smoker,
            _ => BlocksRegistry.Furnace
        };
        await world.SetBlockAsync(position, machine);

        return Assert.IsType<FurnaceBlockEntity>(world.GetResidentBlockEntity(position));
    }

    // The machine's key for a recipe the probe names by its qualified identifier.
    private static string RecipeKey(RecipeSet recipes, string identifier) =>
        recipes.Recipes.Keys.Single(key => AdvancementsRegistry.Qualify(key) == identifier);

    // How vanilla's ExperienceOrb.award splits one recipe's experience into orbs.
    private static IEnumerable<int> Orbs(int experience)
    {
        while (experience > 0)
        {
            var orb = ExperienceOrb.SplitValue(experience);
            experience -= orb;
            yield return orb;
        }
    }

    // The recipes whose vanilla recipe advancement's recipe_unlocked criterion the player has met.
    private static HashSet<string> UnlockedCriteria(Entities.Player player)
    {
        var met = new HashSet<string>();
        foreach (var advancement in AdvancementsRegistry.ForTrigger(RecipeUnlocked))
        {
            foreach (var criterion in advancement.Criteria.Where(criterion => criterion.Trigger == RecipeUnlocked))
            {
                if (!player.Advancements.HasCriterion(advancement.Identifier, criterion.Identifier))
                    continue;

                if (criterion.Conditions.TryGetProperty("recipes", out var recipes)
                    && recipes.ValueKind == JsonValueKind.String)
                {
                    met.Add(AdvancementsRegistry.Qualify(recipes.GetString()!));
                }
            }
        }

        return met;
    }

    // Both sides sorted, as the probe sorts what vanilla gives out in a hash order.
    private static void Compare(List<string> differences, string what, JsonElement expected, IEnumerable<string> actual)
    {
        var vanilla = expected.EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Number ? value.GetInt32().ToString() : value.GetString()!)
            .Order(StringComparer.Ordinal)
            .ToList();
        var obsidian = actual.Order(StringComparer.Ordinal).ToList();

        if (!vanilla.SequenceEqual(obsidian))
            differences.Add($"{what}: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
    }

    private static JsonElement LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.furnace_results.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
