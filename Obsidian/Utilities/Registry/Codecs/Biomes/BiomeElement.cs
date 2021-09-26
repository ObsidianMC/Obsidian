using Obsidian.Nbt;
using Obsidian.Utilities.Converters;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Registry.Codecs.Biomes
{
    public class BiomeElement
    {
        public BiomeEffect Effects { get; set; }

        public float Depth { get; set; }

        public float Temperature { get; set; }

        public float Scale { get; set; }

        public float Downfall { get; set; }

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
