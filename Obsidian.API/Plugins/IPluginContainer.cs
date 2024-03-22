namespace Obsidian.API.Plugins;
public interface IPluginContainer
{
    /// <summary>
    /// Searches for the specified file that was packed alongside your plugin.
    /// </summary>
    /// <param name="fileName">The name of the file you're searching for.</param>
    /// <returns>Null if the file is not found or the byte array of the file.</returns>
    public byte[]? GetFileData(string fileName);
}
