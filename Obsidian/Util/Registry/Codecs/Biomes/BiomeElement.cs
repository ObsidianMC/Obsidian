using Newtonsoft.Json;
using Obsidian.Nbt;
using Obsidian.Util.Converters;
using Obsidian.Util.Extensions;

namespace Obsidian.Util.Registry.Codecs.Biomes
{
    public class BiomeElement
    {
        public BiomeEffect Effects { get; set; }

        [JsonConverter(typeof(DefaultObjectConverter))]
        public float Depth { get; set; }

        [JsonConverter(typeof(DefaultObjectConverter))]
        public float Temperature { get; set; }

        [JsonConverter(typeof(DefaultObjectConverter))]
        public float Scale { get; set; }

        [JsonConverter(typeof(DefaultObjectConverter))]
        public float Downfall { get; set; }

        [JsonConverter(typeof(DefaultObjectConverter))]
        public string Category { get; set; }

        public string Precipitation { get; set; }//TODO turn into enum

        public string TemperatureModifier { get; set; }//TODO turn into enum

        internal void Write(NbtCompound compound)
        {
            var elements = new NbtCompound("element")
            {
                new NbtTag<string>("precipitation", this.Precipitation),

                new NbtTag<float>("depth", this.Depth),
                new NbtTag<float>("temperature", this.Temperature),
                new NbtTag<float>("scale", this.Scale),
                new NbtTag<float>("downfall", this.Downfall),

                new NbtTag<string>("category", this.Category)
            };

            this.Effects.Write(elements);

            if (!this.TemperatureModifier.IsNullOrEmpty())
                elements.Add(new NbtTag<string>("temperature_modifier", this.TemperatureModifier));

            compound.Add(elements);
        }
    }
}
