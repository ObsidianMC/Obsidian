using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.Utilities.Converters;
// ReSharper disable InconsistentNaming

namespace Obsidian.Chat
{
    public class ClickComponent : IClickComponent
    {
        [JsonProperty("action"), JsonConverter(typeof(DefaultEnumConverter<EClickAction>))]
        public EClickAction Action { get; set; }

        [JsonProperty("value")]
        public string Value { get; set; }

        [JsonProperty("translate", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Translate { get; set; }
    }
}