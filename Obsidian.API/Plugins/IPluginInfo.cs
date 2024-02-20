namespace Obsidian.API.Plugins;

public interface IPluginInfo
{
    public string Name { get; }
    public Version Version { get; }
    public string Description { get; }
    public string[] Authors { get; }
    public Uri ProjectUrl { get; }
}
