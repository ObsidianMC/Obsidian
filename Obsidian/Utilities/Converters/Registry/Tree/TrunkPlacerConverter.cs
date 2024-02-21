using Obsidian.API.World.Features.Tree;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Converters.Registry.Tree;
public sealed class TrunkPlacerConverter : JsonConverter<TrunkPlacer>
{
    public override TrunkPlacer? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var element = JsonDocument.ParseValue(ref reader).RootElement;

        //var type = element.GetProperty("type").GetString()!;

        //if (!TreeFeatureRegistry.TrunkPlacers.Values.TryGetValue(type, out var trunkPlacerType))
        //    return null;

        //foreach(var property in trunkPlacerType.GetProperties())
        //{
        //    //We know the policy will always be snake case
        //    var name = options.PropertyNamingPolicy!.ConvertName(property.Name);

        //    if (!element.TryGetProperty(name, out var propertyElement))
        //        throw new JsonException($"Failed to find property with name {name}");

        //    if (property.PropertyType.IsValueType)
        //    {

        //    }
        //}

        return null;
    }

    public override void Write(Utf8JsonWriter writer, TrunkPlacer value, JsonSerializerOptions options) => throw new NotImplementedException();
}
