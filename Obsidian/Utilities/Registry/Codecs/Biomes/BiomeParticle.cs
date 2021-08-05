using Obsidian.Nbt;
using Obsidian.Utilities.Converters;
using System.Text.Json.Serialization;

namespace Obsidian.Utilities.Registry.Codecs.Biomes
{
    public class BiomeParticle
    {
        [JsonConverter(typeof(DefaultObjectConverter))]
        public float Probability { get; set; }

        public BiomeOption Options { get; set; }

        internal void Write(NbtCompound compound)
        {
            var particle = new NbtCompound("particle")
            {
                new NbtTag<float>("probability", this.Probability)
            };

            if (this.Options != null)
            {
                var options = new NbtCompound("options")
                {
                    new NbtTag<string>("type", this.Options.Type)
                };

                particle.Add(options);
            }

        }
    }
}
