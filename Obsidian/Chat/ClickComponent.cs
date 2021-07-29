using Obsidian.API;
using Obsidian.Utilities.Converters;
using System.Text.Json.Serialization;
// ReSharper disable InconsistentNaming

namespace Obsidian.Chat
{
    public class ClickComponent : IClickComponent
    {
        [JsonConverter(typeof(DefaultEnumConverter<EClickAction>))]
        public EClickAction Action { get; set; }

        public string Value { get; set; }

        public string Translate { get; set; }
    }
}