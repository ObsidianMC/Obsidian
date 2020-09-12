using Newtonsoft.Json;

namespace Obsidian.Util.Registry
{
    public class BlockJson
    {
        [JsonProperty("states")]
        public BlockStateJson[] States { get; set; }

        [JsonProperty("properties")]
        public BlockPropertiesExtraJson Properties { get; set; }
    }

}
