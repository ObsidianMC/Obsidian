using Newtonsoft.Json;

namespace Obsidian.Util
{
    public class MojangUser
    {
        [JsonProperty("id")]
        public string Id { get; set; }

        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("legacy")]
        public bool Legacy { get; set; }

        [JsonProperty("demo")]
        public bool Demo { get; set; }
    }
}
