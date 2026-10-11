using System.IO;
using System.Threading;

namespace Obsidian.Plugins;

public sealed class DirectoryWatcher : IDisposable
{
    private ImmutableArray<string> _filters = [];
    public ImmutableArray<string> Filters { get => _filters; set => _filters = value.IsDefault ? [] : value; }

    public event Action<string> FileChanged = default!;
    public event Action<string, string> FileRenamed = default!;
    public event Action<string> FileDeleted = default!;

    private readonly Dictionary<string, FileSystemWatcher> watchers = new();

    // A file being written raises several events; FileChanged fires once it has been quiet this long, so that the
    // listener sees the finished file.
    private static readonly TimeSpan settleDelay = TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentDictionary<string, CancellationTokenSource> pendingUpdates = new();

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
        if (!TestFilter(e.FullPath) || e.ChangeType == WatcherChangeTypes.Deleted)
            return;

        var pending = new CancellationTokenSource();
        var path = e.FullPath;

        this.pendingUpdates.AddOrUpdate(path, pending, (_, previous) =>
        {
            previous.Cancel();
            return pending;
        });

        _ = Task.Delay(settleDelay, pending.Token).ContinueWith(task =>
        {
            if (task.IsCanceled || !this.pendingUpdates.TryRemove(new(path, pending)))
                return;

            FileChanged?.Invoke(path);
        }, TaskScheduler.Default);
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
            OnFileDeleted(sender, new FileSystemEventArgs(WatcherChangeTypes.Deleted,
                Path.GetDirectoryName(e.OldFullPath)!, Path.GetFileName(e.OldFullPath)));
            return;
        }
        // Renamed from non-filtrable to non-filtrable
        else if (!oldFilterTest && !newFilterTest)
        {
            return;
        }

        FileRenamed?.Invoke(e.OldFullPath, e.FullPath);
    }

    private void OnFileDeleted(object sender, FileSystemEventArgs e)
    {
        if (this.pendingUpdates.TryRemove(e.FullPath, out var pending))
            pending.Cancel();

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
