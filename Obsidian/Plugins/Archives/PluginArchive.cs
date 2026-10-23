using System.IO;

namespace Obsidian.Plugins.Archives;

/// <summary>
/// A plugin file read into memory: its metadata and its files (assemblies, and the <c>assets/</c> and <c>data/</c>
/// folders of version 2 archives).
/// </summary>
/// <remarks>
/// The archive keeps the file's bytes rather than the file, so the file can be replaced while the plugin runs, which
/// is how plugins are reloaded. Files are decompressed when read.
/// </remarks>
public abstract class PluginArchive
{
    public string Source { get; internal set; } = string.Empty;

    public required PluginInfo Info { get; init; }

    /// <summary>
    /// The paths of the archive's files, with <c>/</c> separators.
    /// </summary>
    public abstract IEnumerable<string> Paths { get; }

    /// <summary>
    /// Reads a file of the archive.
    /// </summary>
    /// <param name="path">The file's path in the archive, e.g. <c>MyPlugin.dll</c> or
    /// <c>assets/myplugin/lang/en_us.json</c>.</param>
    /// <returns>The file's contents, or null if the archive has no such file.</returns>
    public abstract byte[]? Read(string path);

    /// <summary>
    /// Reads a plugin file, in either archive format.
    /// </summary>
    /// <param name="path">The file to read.</param>
    /// <exception cref="InvalidDataException">The file isn't a valid plugin archive.</exception>
    public static async Task<PluginArchive> OpenAsync(string path)
    {
        var data = await ReadFileAsync(path);

        return Open(path, data);
    }

    internal static PluginArchive Open(string source, byte[] data)
    {
        if (ZipPluginArchive.IsZip(data))
            return ZipPluginArchive.Open(source, data);

        if (LegacyPluginArchive.IsLegacy(data))
            return LegacyPluginArchive.Open(source, data);

        throw new InvalidDataException("Not a plugin archive: it is neither a ZIP file nor begins with OBBY.");
    }

    // A plugin being copied into the folder can still be open for writing when the watcher reports it, so reading is
    // retried for a short while.
    private static async Task<byte[]> ReadFileAsync(string path)
    {
        const int attempts = 10;

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await File.ReadAllBytesAsync(path);
            }
            catch (IOException) when (attempt < attempts && File.Exists(path))
            {
                await Task.Delay(100 * attempt);
            }
        }
    }
}
