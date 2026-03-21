namespace Obsidian.API;

public interface IWorld : ILevel
{
    public string PlayerDataPath { get; }
    public string LevelDataFilePath { get; }
    public IWorldManager WorldManager { get; }
}
