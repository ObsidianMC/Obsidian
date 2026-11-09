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

    public void RandomTick(AbstractLevel level, Vector position, IBlock block, IRandomSource random)
    {
        if (!stages.Value.TryGetValue(block.RegistryId, out var stage) || stage.Next is null)
            return;

        var chest = block.BlockClass() == "WeatheringCopperChestBlock";
        if (block.BlockClass() == "WeatheringCopperDoorBlock" && block.GetProperty("half") == "upper")
            return;

        if (chest && !CanAgeChest(level, position, block))
            return;

        if (!TryWeather(level, position, stage.Age, random))
            return;

        if (!BlockStateProperties.TryGetState(stage.Next, block.GetProperties(), out var next))
            return;

        // Copper chest shape synchronization is not part of Obsidian's generic chest shape updater yet.
        // Resolve the connected half before notifying neighbors, preserving its properties and inventory.
        Vector? partnerPosition = null;
        IBlock? partnerState = null;
        if (chest && block.GetProperty("type") == "left")
        {
            var partner = position.Offset(FeatureHelpers.ParseFace(block.GetProperty("facing")).ClockWise());
            var other = Growth.BlockAt(level, partner);
            if (other.BlockClass() is "CopperChestBlock" or "WeatheringCopperChestBlock"
                && other.GetProperty("type") == "right"
                && other.GetProperty("facing") == block.GetProperty("facing")
                && BlockStateProperties.TryGetState(stage.Next, other.GetProperties(), out var following))
            {
                partnerPosition = partner;
                partnerState = following;
            }
        }

        // The normal mutation path retains compatible block entities and propagates door halves.
        level.BlockUpdates.SetBlock(position, next);
        if (partnerPosition is Vector connected && partnerState is not null)
            level.BlockUpdates.SetBlock(connected, partnerState);
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
            result.Add(block.RegistryId, new Stage(entry.Value.GetProperty("age").GetInt32(), next));
        }

        return result;
    }
}
