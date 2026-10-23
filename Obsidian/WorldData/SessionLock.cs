using System.IO;
using System.Text;

namespace Obsidian.WorldData;

/// <summary>
/// Holds a world folder's <c>session.lock</c> while the world is open, like vanilla's <c>DirectoryLock</c>, so neither
/// the game nor another server opens the world at the same time.
/// </summary>
/// <remarks>
/// Like vanilla, the file holds a snowman and is locked exclusively from start to end; vanilla checks for that lock
/// before opening a world. The file is also opened without sharing, which covers platforms without byte-range locks.
/// </remarks>
internal sealed class SessionLock : IDisposable
{
    private const string FileName = "session.lock";

    private readonly FileStream stream;

    private SessionLock(FileStream stream) => this.stream = stream;

    /// <summary>
    /// Locks <paramref name="folder"/>, creating it if needed.
    /// </summary>
    /// <exception cref="IOException">The world is already open somewhere else.</exception>
    public static SessionLock Acquire(string folder)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, FileName);

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException ex)
        {
            throw new IOException($"The world in {folder} is already open somewhere else ({FileName} is locked).", ex);
        }

        try
        {
            stream.SetLength(0);
            stream.Write(Encoding.UTF8.GetBytes("☃"));
            stream.Flush(flushToDisk: true);

            if (!OperatingSystem.IsMacOS() && !OperatingSystem.IsIOS() && !OperatingSystem.IsTvOS())
                stream.Lock(0, long.MaxValue);
        }
        catch (IOException ex)
        {
            stream.Dispose();
            throw new IOException($"The world in {folder} is already open somewhere else ({FileName} is locked).", ex);
        }

        return new SessionLock(stream);
    }

    public void Dispose() => this.stream.Dispose();
}
