using Newtonsoft.Json;

namespace Obsidian.Utilities.Registry
{
    public class BlockStateJson
    {
        [JsonProperty("id")]
        public int Id { get; set; }

        [JsonProperty("default")]
        public bool Default { get; set; }

        [JsonProperty("properties")]
        public BlockPropertiesJson Properties { get; set; }
    }

}
