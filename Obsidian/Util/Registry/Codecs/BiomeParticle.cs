using Newtonsoft.Json;
using Obsidian.Nbt;
using Obsidian.Nbt.Tags;
using Obsidian.Util.Converters;

namespace Obsidian.Util.Registry.Codecs
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
                new NbtFloat("probability", this.Probability)
            };

            if(this.Options != null)
            {
                var options = new NbtCompound("options")
                {
                    new NbtString("type", this.Options.Type)
                };

                particle.Add(options);
            }

        }
    }
}
