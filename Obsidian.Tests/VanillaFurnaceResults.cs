using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Obsidian.API;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.Net;
using Obsidian.Net.Packets.Play.Serverbound;
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
/// <remarks>
/// In the advancement registry's collection: the replay adds advancements of its own while it runs.
/// </remarks>
[Collection(nameof(PluginTagFiles))]
public sealed class VanillaFurnaceResults(ITestOutputHelper output)
{
    private static readonly JsonElement fixture = LoadFixture();

    private const string RecipeUnlocked = "minecraft:recipe_unlocked";
    private const string RecipeCrafted = "minecraft:recipe_crafted";

    // A cooking machine's result slot.
    private const int ResultSlot = 2;

    // Where the take cases' players stand and the break cases' machines stand, apart so their orbs don't mix.
    private const int TakeRow = 2;
    private const int BreakRow = 13;

    /// <summary>
    /// Each case's machine holds its used recipes and a result. A take case's player clicks the result as a client
    /// does; a break case's machine is removed and the level ticks. The level's cooking random is the case's seed.
    /// The recipes unlocked (recorded as known), the <c>recipe_unlocked</c> and <c>recipe_crafted</c> triggers (by
    /// advancements the replay adds for each recipe), the orbs spawned, the recipes the machine still holds and the
    /// draws of the bonus points must be vanilla's, apart from the known differences named in <see cref="Known"/>.
    /// </summary>
    [Fact(Timeout = TestServerFactory.TestTimeout)]
    public async Task ReplayFurnaceResultReference()
    {
        Assert.Equal(ServerConstants.ProtocolDescription, fixture.GetProperty("version").GetString());

        var cases = fixture.GetProperty("cases").EnumerateArray()
            .Select((probe, index) => (Probe: probe, Index: index))
            .ToArray();

        await using var context = TestServerFactory.CreateServer($"furnace-results-{Guid.NewGuid():N}");
        context.World.RecipeSource = () => context.Server.RecipeRegistry.Current;
        context.World.LevelData.GameRules["spawn_mobs"] = "false";
        context.World.LevelData.GameRules["random_tick_speed"] = "0";
        WorldBlockEntities.AddEmptyChunk(context.World);

        var definitions = AdvancementsRegistry.Current.Values.ToArray();
        try
        {
            AdvancementsRegistry.Apply([.. definitions, .. TriggerAdvancements(cases.Select(item => item.Probe))]);
            await FixtureCases.AssertAllAsync(cases, item => Name(item.Probe),
                item => ReplayAsync(context, item.Probe, item.Index));
        }
        finally
        {
            AdvancementsRegistry.Apply(definitions);
        }

        output.WriteLine($"Replayed {cases.Length} furnace result cases.");
    }

    private static string Name(JsonElement probe) => probe.GetProperty("name").GetString()!;

    private static async Task ReplayAsync(RealServerContext context, JsonElement probe, int index)
    {
        var world = context.World;
        var breaks = probe.GetProperty("action").GetString() == "break";
        var machinePosition = new Vector(index, 64, breaks ? BreakRow : TakeRow + 4);
        var furnace = await PlaceMachineAsync(world, probe, machinePosition);

        // The level's random, which Obsidian's cooking experience is known not to draw from, and the cooking random.
        var levelRandom = new RecordingRandom(probe.GetProperty("seed").GetInt64());
        var cookingRandom = new RecordingRandom(probe.GetProperty("seed").GetInt64());
        world.GrowthRandom = levelRandom;
        world.CookingExperienceRandom = cookingRandom.NextFloat;

        var differences = new List<string>();
        if (breaks)
            await BreakAsync(differences, world, probe, furnace);
        else
            await TakeAsync(differences, context, probe, index, furnace);

        if (breaks)
            Known.BreakBonusDraws(differences, probe, cookingRandom.Draws);
        else
            Compare(differences, "bonus point draws", VanillaBonusDraws(probe), cookingRandom.Draws.Select(Text));

        Known.LevelRandomDraws(differences, probe, levelRandom.Draws);

        if (differences.Count > 0)
            throw new Xunit.Sdk.XunitException(string.Join("\n", differences));
    }

    // A player who knows the case's known recipes clicks the machine's result.
    private static async Task TakeAsync(
        List<string> differences,
        RealServerContext context,
        JsonElement probe,
        int index,
        FurnaceBlockEntity furnace)
    {
        var position = new VectorD(index + 0.5, 70, TakeRow + 0.5);
        var player = TestServerFactory.CreateConnectedPlayer(context.Server, context.World, $"Cook{index}", position);
        await TestServerFactory.JoinConnectedPlayerAsync(context.Server, player);
        foreach (var known in probe.GetProperty("known").EnumerateArray())
            await player.UnlockProgressRecipeAsync(known.GetString()!);

        var knownBefore = KnownRecipes(player);
        var unlockedBefore = Met(player, RecipeUnlocked);
        var craftedBefore = Met(player, RecipeCrafted);

        furnace.Inventory[ResultSlot] = new ItemStack(ItemsRegistry.Stone, 1);
        await player.OpenInventoryAsync(furnace.Furnace);
        await ClickResultAsync(player);

        // The recipes come on the player's next progress tick, or as they leave, whichever is first. No level ticks
        // here, so the player leaves: that gives them under the same gate, the same way.
        await player.StopTickingAsync();

        var unlocked = KnownRecipes(player).Except(knownBefore);
        Compare(differences, "recipes unlocked", Strings(probe, "unlocked"), unlocked);
        var unlockedNow = Met(player, RecipeUnlocked).Except(unlockedBefore);
        var craftedNow = Met(player, RecipeCrafted).Except(craftedBefore);
        CompareTriggers(differences, probe, RecipeUnlocked, "unlocked", unlockedNow);
        CompareTriggers(differences, probe, RecipeCrafted, "crafted", craftedNow);

        var orbs = Orbs(context.World, player.Position);
        Compare(differences, "orbs", Strings(probe, "orbs"), orbs.Select(orb => orb.Value.ToString()));
        foreach (var orb in orbs)
            await orb.RemoveAsync();

        BlockProbes.Expect(differences, "used after", probe.GetProperty("usedAfter").GetInt32(),
            furnace.RecipesUsed.Count);
    }

    // The machine is removed, as breaking it does, and the level ticks to run the removal.
    private static async Task BreakAsync(
        List<string> differences,
        WorldData.World world,
        JsonElement probe,
        FurnaceBlockEntity furnace)
    {
        await world.SetBlockAsync(furnace.BlockPosition, BlocksRegistry.Air);
        await world.DoWorldTickAsync();

        // Vanilla gives the recipes to no one; it pops their experience at the machine. The orbs have had a tick to
        // move, so they're looked for around it.
        var orbs = Orbs(world, (VectorD)furnace.BlockPosition, 1);
        Compare(differences, "orbs", Strings(probe, "orbs"), orbs.Select(orb => orb.Value.ToString()));
        Compare(differences, "recipes awarded", Strings(probe, "awarded"), []);
        foreach (var orb in orbs)
            await orb.RemoveAsync();
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

        var furnace = Assert.IsType<FurnaceBlockEntity>(world.GetResidentBlockEntity(position));
        var recipes = world.CookingRecipes;
        foreach (var used in probe.GetProperty("used").EnumerateObject())
            furnace.RecipesUsed[RecipeKey(recipes, used.Name)] = used.Value.GetInt32();

        return furnace;
    }

    // Clicks the open machine's result slot, as the client sends it.
    private static async Task ClickResultAsync(Entities.Player player)
    {
        using var writer = new NetworkBuffer();
        writer.WriteVarInt(player.CurrentContainerId);
        writer.WriteVarInt(0);
        writer.WriteShort(ResultSlot);
        writer.WriteByte(0);
        writer.WriteVarInt((int)ClickType.Pickup);
        writer.WriteVarInt(0);
        writer.WriteBoolean(false);

        using var reader = new NetworkBuffer(writer.AsSpan(0, writer.Size).ToArray());
        var packet = new ContainerClickPacket();
        packet.Populate(reader);
        await packet.HandleAsync(player.Server, player);
    }

    // The trigger fired as often as vanilla fired it, for vanilla's recipes.
    private static void CompareTriggers(
        List<string> differences,
        JsonElement probe,
        string trigger,
        string recipes,
        IEnumerable<string> met)
    {
        var fired = probe.GetProperty("triggers").EnumerateArray().Count(value => value.GetString() == trigger);
        var observed = met.ToList();

        Compare(differences, $"{trigger} recipes", Strings(probe, recipes), observed);
        BlockProbes.Expect(differences, $"{trigger} count", fired, observed.Count);
    }

    // The recipes the player's save lists as known.
    private static HashSet<string> KnownRecipes(Entities.Player player)
    {
        var known = new HashSet<string>();
        if (player.SaveData().TryGetTag<NbtCompound>("recipeBook", out var book)
            && book.TryGetTag<NbtList>("recipes", out var recipes))
        {
            foreach (var recipe in recipes.OfType<NbtTag<string>>())
                known.Add(recipe.Value);
        }

        return known;
    }

    // The recipes whose replay advancement for the trigger the player has met.
    private static HashSet<string> Met(Entities.Player player, string trigger)
    {
        var met = new HashSet<string>();
        foreach (var advancement in AdvancementsRegistry.Current.Values)
        {
            if (!advancement.Identifier.StartsWith(Prefix(trigger), StringComparison.Ordinal)
                || !player.Advancements.HasCriterion(advancement.Identifier, "fired"))
            {
                continue;
            }

            var path = advancement.Identifier[Prefix(trigger).Length..];
            met.Add($"minecraft:{path}");
        }

        return met;
    }

    // One advancement per recipe and trigger, met when the trigger fires for that recipe.
    private static IEnumerable<API.Advancements.Advancement> TriggerAdvancements(IEnumerable<JsonElement> cases)
    {
        var recipes = cases
            .SelectMany(probe => probe.GetProperty("used").EnumerateObject().Select(used => used.Name))
            .Distinct();
        foreach (var recipe in recipes)
        {
            foreach (var trigger in new[] { RecipeUnlocked, RecipeCrafted })
            {
                var json = $$"""
                    { "criteria": { "fired": { "trigger": "{{trigger}}", "conditions": { "recipes": "{{recipe}}" } } } }
                    """;
                using var document = JsonDocument.Parse(json);
                var path = recipe["minecraft:".Length..];
                yield return AdvancementsRegistry.Parse(Prefix(trigger) + path, document.RootElement);
            }
        }
    }

    private static string Prefix(string trigger) => $"probe:{trigger["minecraft:".Length..]}/";

    private static List<ExperienceOrb> Orbs(WorldData.World world, VectorD position, float range = 0.01f) =>
        world.GetNonPlayerEntitiesInRange(position, range).OfType<ExperienceOrb>().ToList();

    // The machine's key for a recipe the probe names by its qualified identifier.
    private static string RecipeKey(RecipeSet recipes, string identifier) =>
        recipes.Recipes.Keys.Single(key => AdvancementsRegistry.Qualify(key) == identifier);

    // Vanilla's chance draws for the fractions of a point, its nextFloats.
    private static IEnumerable<string> VanillaBonusDraws(JsonElement probe) =>
        probe.GetProperty("draws").EnumerateArray()
            .Select(Draw.Parse)
            .Where(draw => draw.Call == "nextFloat")
            .Select(Text);

    private static string Text(Draw draw) => draw.ToString();

    private static IEnumerable<string> Strings(JsonElement probe, string name) =>
        probe.GetProperty(name).EnumerateArray()
            .Select(value => value.ValueKind == JsonValueKind.Number ? value.GetInt32().ToString() : value.GetString()!);

    // Both sides sorted: vanilla gives its recipes out in a hash order, so the probe sorts them.
    private static void Compare(List<string> differences, string what, IEnumerable<string> expected,
        IEnumerable<string> actual)
    {
        var vanilla = expected.Order(StringComparer.Ordinal).ToList();
        var obsidian = actual.Order(StringComparer.Ordinal).ToList();

        if (!vanilla.SequenceEqual(obsidian))
            differences.Add($"{what}: vanilla [{string.Join(", ", vanilla)}], Obsidian [{string.Join(", ", obsidian)}]");
    }

    /// <summary>
    /// Where Obsidian knowingly differs from vanilla, each asserted as it is, so that the replay fails when the
    /// difference goes away and the check can become a comparison (see docs/known-gaps/workstation-details.md).
    /// </summary>
    private static class Known
    {
        /// <summary>
        /// Taking a result or breaking a machine draws nothing from the level's random. Vanilla draws a fraction's
        /// chance from it, a check for orbs to merge with for each orb and, breaking, a drop position for each slot;
        /// Obsidian draws the chance from the level's cooking random (compared separately) and the rest not at all.
        /// </summary>
        public static void LevelRandomDraws(List<string> differences, JsonElement probe, IReadOnlyList<Draw> obsidian)
        {
            if (obsidian.Count > 0)
            {
                differences.Add("known difference closed: the experience now draws from the level's random "
                    + $"[{string.Join(", ", obsidian)}]; compare it with vanilla's draws instead");
            }

            // That vanilla does make those draws, one merge check per orb, which this difference stands for.
            var orbs = probe.GetProperty("orbs").GetArrayLength();
            var merges = probe.GetProperty("draws").EnumerateArray()
                .Select(Draw.Parse)
                .Count(draw => draw.Call == "nextInt" && draw.Bound == 40);
            if (merges != orbs)
                differences.Add($"vanilla made {merges} orb merge draws for {orbs} orbs; this check is out of date");
        }

        /// <summary>
        /// A broken machine draws its bonus points as often as vanilla, but not vanilla's values: vanilla first draws
        /// a drop position for each slot (three nextDoubles each, empty slots included), so its chance comes later in
        /// its sequence, while Obsidian's are the first floats of the cooking random. Obsidian's are pinned to those.
        /// </summary>
        public static void BreakBonusDraws(List<string> differences, JsonElement probe, IReadOnlyList<Draw> obsidian)
        {
            var vanilla = VanillaBonusDraws(probe).ToList();
            var fresh = new RecordingRandom(probe.GetProperty("seed").GetInt64());
            for (var draw = 0; draw < vanilla.Count; draw++)
                fresh.NextFloat();

            var expected = fresh.Draws.Select(Text).ToList();
            Compare(differences, "broken machine's bonus point draws", expected, obsidian.Select(Text));

            var drops = probe.GetProperty("draws").EnumerateArray()
                .TakeWhile(draw => Draw.Parse(draw).Call == "nextDouble")
                .Count();
            if (drops == 0 || vanilla.Count > 0 && vanilla.SequenceEqual(expected))
            {
                differences.Add("known difference closed: vanilla's broken machine no longer draws its slots' drop "
                    + "positions first; compare the bonus point draws with vanilla's instead");
            }
        }
    }

    private static JsonElement LoadFixture()
    {
        using var stream = Assembly.GetExecutingAssembly()
            .GetManifestResourceStream("Obsidian.Tests.Assets.furnace_results.json")!;
        using var document = JsonDocument.Parse(stream);
        return document.RootElement.Clone();
    }
}
