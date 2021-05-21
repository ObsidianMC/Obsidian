using Newtonsoft.Json;
using Obsidian.Nbt;
using Obsidian.Util.Converters;

namespace Obsidian.Util.Registry.Codecs.Biomes
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
