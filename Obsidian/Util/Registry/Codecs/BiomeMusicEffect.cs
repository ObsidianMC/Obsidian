using Newtonsoft.Json;
using Obsidian.Util.Converters;

namespace Obsidian.Util.Registry.Codecs
{
    public class BiomeMusicEffect
    {
        [JsonConverter(typeof(DefaultObjectConverter))]
        public bool ReplaceCurrentMusic { get; set; }

        public int MaxDelay { get; set; }

        public string Sound { get; set; }

        public int MinDelay { get; set; }
    }
}
