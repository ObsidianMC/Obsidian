using Newtonsoft.Json;
using Obsidian.Nbt.Tags;
using Obsidian.Utilities.Converters;
using Obsidian.Utilities.Extensions;

namespace Obsidian.Utilities.Registry.Codecs.Biomes
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
                new NbtString("precipitation", this.Precipitation),

                new NbtFloat("depth", this.Depth),
                new NbtFloat("temperature", this.Temperature),
                new NbtFloat("scale", this.Scale),
                new NbtFloat("downfall", this.Downfall),

                new NbtString("category", this.Category)
            };

            this.Effects.Write(elements);

            if (!this.TemperatureModifier.IsNullOrEmpty())
                elements.Add(new NbtString("temperature_modifier", this.TemperatureModifier));

            compound.Add(elements);
        }
    }
}
