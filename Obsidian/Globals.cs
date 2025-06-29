using Obsidian.Utilities.Converters;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian;

public static class Globals
{
    public static HttpClient HttpClient { get; } = new();
    public static XorshiftRandom Random { get; } = new();

    public static readonly JsonSerializerOptions RegistryJsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters =
        {
            new JsonStringEnumConverter<DamageScaling>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<DamageEffects>(JsonNamingPolicy.SnakeCaseLower),
            new JsonStringEnumConverter<DeathMessageType>(JsonNamingPolicy.SnakeCaseLower),
        }
    };

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters =
            {
                new CraftingTypeConverter(),
                new IngredientConverter(),
                new DefaultEnumConverter<HoverAction>(),
                new DefaultEnumConverter<ClickAction>(),
                new DefaultEnumConverter<CraftingBookCategory>(),
                new DefaultEnumConverter<CookingBookCategory>(),
                new RecipesConverter(),
                new HexColorConverter(),
                new GuidJsonConverter()
            },

        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };
}

file class GuidJsonConverter : JsonConverter<Guid>
{
    public override Guid Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        Guid.Parse(reader.GetString()!);

    public override void Write(Utf8JsonWriter writer, Guid value, JsonSerializerOptions options) => writer.WriteStringValue(value.ToString("N"));
}
