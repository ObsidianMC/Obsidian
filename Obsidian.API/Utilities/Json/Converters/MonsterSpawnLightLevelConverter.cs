using Obsidian.API.Registry.Codecs.Dimensions;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian.API.Utilities.Json.Converters;
public sealed class MonsterSpawnLightLevelConverter : JsonConverter<IMonsterSpawnLightLevel>
{
    public override IMonsterSpawnLightLevel? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var doc = JsonDocument.ParseValue(ref reader);
        var element = doc.RootElement;

        if (element.ValueKind == JsonValueKind.Object)
            return element.GetProperty("value").Deserialize<MonsterLightLevelUniformValue>(options);

        return new MonsterSpawnLightLevelIntValue { Value = element.GetInt32() };
    }

    public override void Write(Utf8JsonWriter writer, IMonsterSpawnLightLevel value, JsonSerializerOptions options)
        => throw new NotImplementedException();
}
