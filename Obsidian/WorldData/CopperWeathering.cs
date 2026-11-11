using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Registries;
using Obsidian.WorldData.Features;
using System.Text.Json;

namespace Obsidian.WorldData;

internal static partial class RandomTickBehaviors
{
    private static readonly IRandomTickBehavior CopperWeathering = new CopperWeatheringBehavior();

    internal static IRandomTickBehavior WeatheringCopperFullBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperSlabBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperStairBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperDoorBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperTrapDoorBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperGrateBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperBulbBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperBarsBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperChainBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperChestBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringCopperGolemStatueBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringLanternBlock => CopperWeathering;

    internal static IRandomTickBehavior WeatheringLightningRodBlock => CopperWeathering;
}

/// <summary>Ages copper using the jar's stage map, keeping properties and resident block entities.</summary>
internal sealed class CopperWeatheringBehavior : IRandomTickBehavior
{
    private sealed record Stage(int Age, string? Next);

    private static readonly Lazy<Dictionary<int, Stage>> stages = new(LoadStages);

    // Each stage's block by the name of the stage after it, for going back a stage.
    private static readonly Lazy<Dictionary<string, string>> previousStages = new(LoadPreviousStages);

    private const string WaxedPrefix = "minecraft:waxed_";

    /// <summary>Whether <paramref name="block"/> weathers, as vanilla's <c>instanceof WeatheringCopper</c>.</summary>
    internal static bool IsWeathering(IBlock block) => stages.Value.ContainsKey(block.RegistryId);

    /// <summary>
    /// Whether <paramref name="block"/> is waxed copper, which vanilla's <c>HoneycombItem.WAX_OFF_BY_BLOCK</c> lists:
    /// the waxed form of a block that weathers.
    /// </summary>
    internal static bool IsWaxed(IBlock block)
    {
        var name = block.UnlocalizedName;
        if (!name.StartsWith(WaxedPrefix, StringComparison.Ordinal))
            return false;

        var unwaxed = "minecraft:" + name[WaxedPrefix.Length..];
        return BlockStateProperties.TryGetState(unwaxed, null, out var plain) && IsWeathering(plain);
    }

    /// <summary>
    /// The stage before <paramref name="block"/>'s, with its properties, or null for unweathered copper (vanilla's
    /// <c>WeatheringCopper.getPrevious</c>).
    /// </summary>
    internal static IBlock? Previous(IBlock block)
    {
        if (!previousStages.Value.TryGetValue(block.UnlocalizedName, out var previous))
            return null;

        return BlockStateProperties.TryGetState(previous, block.GetProperties(), out var state) ? state : null;
    }

    /// <summary>
    /// The unweathered stage of <paramref name="block"/>, with its properties (vanilla's
    /// <c>WeatheringCopper.getFirst</c>).
    /// </summary>
    internal static IBlock First(IBlock block)
    {
        var first = block;
        while (Previous(first) is IBlock previous)
            first = previous;

        return first;
    }

    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!stages.Value.TryGetValue(block.RegistryId, out var stage) || stage.Next is null)
            return;

        var blockClass = block.ClassId();
        var chest = blockClass == BlockClassId.WeatheringCopperChestBlock;
        if (blockClass == BlockClassId.WeatheringCopperDoorBlock && block.GetProperty("half") == "upper")
            return;

        if (chest && !CanAgeChest(level, position, block))
            return;

        if (!TryWeather(level, position, stage.Age, random))
            return;

        if (!BlockStateProperties.TryGetState(stage.Next, block.GetProperties(), out var next))
            return;

        // Shape updates synchronize connected halves and honor re-entrant plugin mutations of those neighbors.
        // The normal mutation path also retains compatible block entities.
        level.BlockUpdates.SetBlock(position, next);
    }

    private static bool CanAgeChest(AbstractLevel level, Vector position, IBlock block)
    {
        if (block.GetProperty("type") == "right"
            || level.GetResidentBlockEntity(position) is not ContainerBlockEntity entity)
        {
            return false;
        }

        return entity.Inventory.GetViewers().Length == 0;
    }

    private static bool TryWeather(AbstractLevel level, Vector position, int age, IRandomSource random)
    {
        // Matches 26.3 ChangeOverTimeBlock's Manhattan radius, float arithmetic and draw order, with
        // WeatheringCopper's fresh-copper modifier. The shared chunk random must retain vanilla's sequence:
        // one gate draw, no second draw if younger copper is found, otherwise one probability draw.
        if (random.NextFloat() >= 0.05688889f)
            return false;

        var same = 0;
        var older = 0;
        foreach (var neighbor in FeatureHelpers.WithinManhattan(position, 4, 4, 4))
        {
            var distance = FeatureHelpers.DistManhattan(position, neighbor);
            if (distance > 4)
                break;

            if (distance == 0
                || !stages.Value.TryGetValue(Growth.BlockAt(level, neighbor).RegistryId, out var surrounding))
            {
                continue;
            }

            if (surrounding.Age < age)
                return false;

            if (surrounding.Age == age)
                same++;
            else
                older++;
        }

        var proportion = (older + 1f) / (older + same + 1f);
        var probability = proportion * proportion * (age == 0 ? 0.75f : 1f);
        return random.NextFloat() < probability;
    }

    private static Dictionary<string, string> LoadPreviousStages()
    {
        var previous = new Dictionary<string, string>();
        foreach (var (id, stage) in stages.Value)
        {
            if (stage.Next is not null)
                previous[stage.Next] = BlocksRegistry.GetDefault(id).UnlocalizedName;
        }

        return previous;
    }

    private static Dictionary<int, Stage> LoadStages()
    {
        using var stream = typeof(CopperWeatheringBehavior).Assembly
            .GetManifestResourceStream("Obsidian.Assets.copper_weathering.json")
            ?? throw new InvalidOperationException("Missing copper weathering asset.");
        using var document = JsonDocument.Parse(stream);
        var result = new Dictionary<int, Stage>();
        foreach (var entry in document.RootElement.EnumerateObject())
        {
            if (!BlockStateProperties.TryGetState(entry.Name, null, out var block))
                throw new InvalidOperationException($"Unknown copper block {entry.Name}.");

            var next = entry.Value.TryGetProperty("next", out var value) ? value.GetString() : null;
            if (next is not null && !BlockStateProperties.TryGetState(next, null, out _))
                throw new InvalidOperationException($"Unknown copper next stage {next} for {entry.Name}.");

            result.Add(block.RegistryId, new Stage(entry.Value.GetProperty("age").GetInt32(), next));
        }

        return result;
    }
}
