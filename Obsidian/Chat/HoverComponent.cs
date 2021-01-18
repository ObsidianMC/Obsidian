using Newtonsoft.Json;
using Obsidian.API;
using Obsidian.Util.Converters;

namespace Obsidian.Chat
{
    public class HoverComponent : IHoverComponent
    {
        [JsonProperty("action"), JsonConverter(typeof(DefaultEnumConverter<EHoverAction>))]
        public EHoverAction Action { get; set; }

        [JsonProperty("contents")]
        public object Contents { get; set; }

        [JsonProperty("translate", DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Translate { get; set; }
    }
}
