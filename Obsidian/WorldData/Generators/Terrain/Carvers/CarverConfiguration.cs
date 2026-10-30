using Obsidian.API.World.Generator.RandomSources;
using System.IO;
using System.Text.Json;
using FloatProvider = Obsidian.WorldData.Generators.Mojang.Carvers.FloatProvider;
using HeightProvider = Obsidian.WorldData.Generators.Mojang.Carvers.HeightProvider;

namespace Obsidian.WorldData.Generators.Terrain.Carvers;

/// <summary>
/// 26.3 carvers describe geometry only. Fluid selection, the uncarvable tag and exposed surface repair belong to
/// terrain's mask application, rather than the old configured carver's lava level and replaceable blocks.
/// </summary>
internal abstract class CarverConfiguration
{
    public required float Probability { get; init; }

    public required HeightProvider Y { get; init; }

    public static CarverConfiguration Read(JsonElement json)
    {
        var probability = json.GetProperty("probability").GetSingle();
        var y = HeightProvider.Parse(json.GetProperty("y"));

        switch (json.GetProperty("type").GetString())
        {
            case "minecraft:cave":
                return new CaveCarverConfiguration
                {
                    Probability = probability,
                    Y = y,
                    Count = CarverCount.Read(json.GetProperty("count")),
                    Thickness = FloatProvider.Parse(json.GetProperty("thickness")),
                    WeirdThicknessBias = json.TryGetProperty("weird_thickness_bias", out var bias) && bias.GetBoolean(),
                    RoomVerticalRadiusMultiplier = FloatProvider.Parse(
                        json.GetProperty("room_vertical_radius_multiplier")),
                    HorizontalRadiusMultiplier = FloatProvider.Parse(json.GetProperty("horizontal_radius_multiplier")),
                    VerticalRadiusMultiplier = FloatProvider.Parse(json.GetProperty("vertical_radius_multiplier")),
                    StartVerticalRadiusMultiplier = OptionalFloat(json, "start_vertical_radius_multiplier", 1.0f),
                    FloorLevel = FloatProvider.Parse(json.GetProperty("floor_level"))
                };
            case "minecraft:canyon":
                var shape = json.GetProperty("shape");
                return new CanyonCarverConfiguration
                {
                    Probability = probability,
                    Y = y,
                    VerticalRotation = FloatProvider.Parse(json.GetProperty("vertical_rotation")),
                    DistanceFactor = FloatProvider.Parse(shape.GetProperty("distance_factor")),
                    Thickness = FloatProvider.Parse(shape.GetProperty("thickness")),
                    YScale = FloatProvider.Parse(shape.GetProperty("y_scale")),
                    WidthSmoothness = shape.GetProperty("width_smoothness").GetInt32(),
                    HorizontalRadiusFactor = FloatProvider.Parse(shape.GetProperty("horizontal_radius_factor")),
                    VerticalRadiusDefaultFactor = shape.GetProperty("vertical_radius_default_factor").GetSingle(),
                    VerticalRadiusCenterFactor = shape.GetProperty("vertical_radius_center_factor").GetSingle()
                };
            default:
                throw new InvalidDataException($"Unknown 26.3 carver {json.GetProperty("type")}.");
        }
    }

    private static FloatProvider OptionalFloat(JsonElement json, string name, float fallback)
    {
        if (json.TryGetProperty(name, out var value))
        {
            return FloatProvider.Parse(value);
        }

        return FloatProvider.Parse(JsonSerializer.SerializeToElement(fallback));
    }
}

internal sealed class CaveCarverConfiguration : CarverConfiguration
{
    public required CarverCount Count { get; init; }

    public required FloatProvider Thickness { get; init; }

    public required bool WeirdThicknessBias { get; init; }

    public required FloatProvider RoomVerticalRadiusMultiplier { get; init; }

    public required FloatProvider HorizontalRadiusMultiplier { get; init; }

    public required FloatProvider VerticalRadiusMultiplier { get; init; }

    public required FloatProvider StartVerticalRadiusMultiplier { get; init; }

    public required FloatProvider FloorLevel { get; init; }
}

internal sealed class CanyonCarverConfiguration : CarverConfiguration
{
    public required FloatProvider VerticalRotation { get; init; }

    public required FloatProvider DistanceFactor { get; init; }

    public required FloatProvider Thickness { get; init; }

    public required FloatProvider YScale { get; init; }

    public required int WidthSmoothness { get; init; }

    public required FloatProvider HorizontalRadiusFactor { get; init; }

    public required float VerticalRadiusDefaultFactor { get; init; }

    public required float VerticalRadiusCenterFactor { get; init; }
}

/// <summary>The integer providers used by vanilla's 26.3 cave counts.</summary>
internal sealed class CarverCount(string type, int min, int max)
{
    public int Sample(IRandomSource random)
    {
        var bound = max - min + 1;

        return type switch
        {
            "minecraft:constant" => min,
            "minecraft:uniform" => min + random.NextInt(bound),
            "minecraft:biased_to_bottom" => min + random.NextInt(random.NextInt(bound) + 1),
            "minecraft:very_biased_to_bottom" => min + random.NextInt(random.NextInt(random.NextInt(bound) + 1) + 1),
            _ => throw new InvalidDataException($"Unknown cave count provider {type}.")
        };
    }

    public static CarverCount Read(JsonElement json)
    {
        if (json.ValueKind == JsonValueKind.Number)
        {
            var value = json.GetInt32();
            return new CarverCount("minecraft:constant", value, value);
        }

        var type = json.GetProperty("type").GetString()!;

        if (type == "minecraft:constant")
        {
            var value = json.GetProperty("value").GetInt32();
            return new CarverCount(type, value, value);
        }

        return new CarverCount(
            type,
            json.GetProperty("min_inclusive").GetInt32(),
            json.GetProperty("max_inclusive").GetInt32());
    }
}
