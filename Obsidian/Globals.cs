using Microsoft.Extensions.Logging;
using Obsidian.API;
using Obsidian.Utilities;
using Obsidian.Utilities.Converters;
using System.Net.Http;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Obsidian
{
    public static class Globals
    {
        public static HttpClient HttpClient { get; } = new HttpClient();
        public static XorshiftRandom Random { get; } = XorshiftRandom.Create();
        public static Config Config { get; set; }
        public static ILogger PacketLogger { get; set; }

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
                new DefaultEnumConverter<EHoverAction>(),
                new DefaultEnumConverter<EClickAction>(),
                new HexColorConverter()
            },
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        };
    }

    public class SnakeCaseNamingPolicy : JsonNamingPolicy
    {
        public static SnakeCaseNamingPolicy Instance { get; } = new SnakeCaseNamingPolicy();

        public override string ConvertName(string name) => name.ToSnakeCase();
    }
}
