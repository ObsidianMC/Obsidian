using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.Util.Converters;
// ReSharper disable InconsistentNaming

namespace Obsidian.Chat
{
    public class TextComponent : ITextComponent
    {
        [JsonProperty("action"), JsonConverter(typeof(DefaultEnumConverter<ETextAction>))]
        public ETextAction Action { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("translate", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Translate { get; set; }
    }
}