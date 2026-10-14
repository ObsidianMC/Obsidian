using Obsidian.API.Data;
using System.IO;
using System.Threading;

namespace Obsidian.WorldData;

public abstract partial class AbstractLevel
{
    private SavedDataStorage? levelSavedData;
    private SavedDataStorage? worldSavedData;

    public SavedData<TValue> GetSavedData<TValue>(
        SavedDataType<TValue> type, Func<TValue> create, SavedDataScope scope = SavedDataScope.Level)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(create);
        if (scope is not (SavedDataScope.Level or SavedDataScope.World))
            throw new ArgumentOutOfRangeException(nameof(scope));

        if (scope == SavedDataScope.World && this is IDimension dimension)
            return dimension.ParentWorld.GetSavedData(type, create, scope);

        if (string.IsNullOrEmpty(this.FolderPath))
            throw new InvalidOperationException("The level must be initialized before accessing saved data.");

        // The overworld is also a level. Reserve an engine directory for world-wide data to keep scopes distinct.
        var storage = scope == SavedDataScope.World
            ? LazyInitializer.EnsureInitialized(ref this.worldSavedData,
                () => new SavedDataStorage(Path.Combine(this.FolderPath, "data", "obsidian", "world")))
            : LazyInitializer.EnsureInitialized(ref this.levelSavedData,
                () => new SavedDataStorage(Path.Combine(this.FolderPath, "data")));

        return storage.GetOrCreate(type, create);
    }

    protected void SavePluginData()
    {
        this.levelSavedData?.Save();
        this.worldSavedData?.Save();
    }
}
