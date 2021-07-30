using Obsidian.Utilities.Converters;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Registry.Codecs.Biomes
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
