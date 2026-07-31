using Obsidian.API.Registry.Codecs.Dimensions;

namespace Obsidian.API;

public interface IWorld : ILevel
{
    public string PlayerDataPath { get; }
    public IWorldManager WorldManager { get; }

    public void RegisterDimension(DimensionCodec codec, IDimension dimension);
}
