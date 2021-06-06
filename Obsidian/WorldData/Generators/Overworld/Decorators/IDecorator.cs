using Obsidian.API;
using Obsidian.WorldData.Generators.Overworld.BiomeNoise;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public interface IDecorator
    {
        void Decorate(Chunk chunk, Vector position, BaseBiomeNoise noise);
    }
}
