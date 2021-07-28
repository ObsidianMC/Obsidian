using Obsidian.API;
using Obsidian.Utilities.Converters;
using System.Text.Json.Serialization;

namespace Obsidian.Chat
{
    public class HoverComponent : IHoverComponent
    {
        [JsonConverter(typeof(DefaultEnumConverter<EHoverAction>))]
        public EHoverAction Action { get; set; }

        public object Contents { get; set; }

        public string Translate { get; set; }
    }
}
