using System.IO;

namespace Obsidian.Plugins;

public sealed class DirectoryWatcher : IDisposable
{
    private string[] _filters = [];
    public string[] Filters { get => _filters; set => _filters = value ?? []; }

    public event Action<string> FileChanged = default!;
    public event Action<string, string> FileRenamed = default!;
    public event Action<string> FileDeleted = default!;

    private readonly Dictionary<string, FileSystemWatcher> watchers = new();
    private readonly Dictionary<string, DateTime> updateTimestamps = new();

    private const double minUpdateInterval = 3.0;

    public void Watch(string path)
    {
        if (!Directory.Exists(path))
            throw new DirectoryNotFoundException(path);

        lock (watchers)
        {
            if (watchers.ContainsKey(path))
                return;
        }

        var watcher = new FileSystemWatcher()
        {
            Path = path,
            EnableRaisingEvents = true,
            IncludeSubdirectories = true,
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName
        };

        watcher.Created += OnFileUpdated;
        watcher.Changed += OnFileUpdated;
        watcher.Renamed += OnFileRenamed;
        watcher.Deleted += OnFileDeleted;

        lock (watchers)
        {
            watchers.Add(path, watcher);
        }
    }

    public void Unwatch(string path)
    {
        lock (watchers)
        {
            if (!watchers.TryGetValue(path, out FileSystemWatcher? watcher))
                throw new KeyNotFoundException();

            watcher.Created -= OnFileUpdated;
            watcher.Changed -= OnFileUpdated;
            watcher.Renamed -= OnFileRenamed;
            watcher.Deleted -= OnFileDeleted;
            watcher.Dispose();

            watchers.Remove(path);
        }
    }

    private void OnFileUpdated(object sender, FileSystemEventArgs e)
    {
        if (!TestFilter(e.FullPath) || !(e.ChangeType == WatcherChangeTypes.Created || e.ChangeType == WatcherChangeTypes.Changed))
            return;

        if (updateTimestamps.TryGetValue(e.FullPath, out DateTime timestamp))
        {
            var now = DateTime.Now;
            if ((now - timestamp).TotalSeconds >= minUpdateInterval)
            {
                updateTimestamps[e.FullPath] = now;
            }
            else
            {
                return;
            }
        }
        else
        {
            updateTimestamps.Add(e.FullPath, DateTime.Now);
        }

        FileChanged?.Invoke(e.FullPath);
    }

    private void OnFileRenamed(object sender, RenamedEventArgs e)
    {
        bool oldFilterTest = TestFilter(e.OldFullPath);
        bool newFilterTest = TestFilter(e.FullPath);

        // Renamed from non-filtrable to filtrable
        if (!oldFilterTest && newFilterTest)
        {
            OnFileUpdated(sender, e);
            return;
        }
        // Renamed from filtrable to non-filtrable
        else if (oldFilterTest && !newFilterTest)
        {
            OnFileDeleted(sender, e);
            return;
        }
        // Renamed from non-filtrable to non-filtrable
        else if (!oldFilterTest && !newFilterTest)
        {
            return;
        }

        if (updateTimestamps.TryGetValue(e.OldFullPath, out DateTime timestamp))
        {
            updateTimestamps.Remove(e.OldFullPath);
            updateTimestamps.Add(e.FullPath, timestamp);
        }

        FileRenamed?.Invoke(e.OldFullPath, e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        updateTimestamps.Remove(e.FullPath);
        FileDeleted?.Invoke(e.FullPath);
    }

    private bool TestFilter(string path)
    {
        return _filters.Length == 0 || _filters.Contains(Path.GetExtension(path));
    }

    public void Dispose()
    {
        foreach (var (path, watcher) in watchers)
        {
            watcher.Created -= OnFileUpdated;
            watcher.Changed -= OnFileUpdated;
            watcher.Renamed -= OnFileRenamed;
            watcher.Deleted -= OnFileDeleted;
            watcher.Dispose();
        }
        watchers.Clear();
    }
}
