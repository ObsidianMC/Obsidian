using Obsidian.Util.DataTypes;

namespace Obsidian.WorldData.Generators.Overworld.Decorators
{
    public interface IDecorator
    {
        void Decorate(Chunk chunk, Position pos, OverworldNoise noise);
    }
}
