namespace Obsidian.Utilities.Collection;

public sealed class DataArray
{
    public int BitsPerEntry => bitsPerEntry;
    public int Capacity => entriesCount;

    internal long[] storage;

    private readonly int bitsPerEntry;
    private readonly int entriesCount;

    private readonly long entryMask;
    private readonly int entriesPerStorageElement;

    public DataArray(int bitsPerEntry, int entriesCount, bool initializeStorage = true)
    {
        const int StorageElementSize = 64;

        this.bitsPerEntry = bitsPerEntry;
        this.entriesCount = entriesCount;

        entryMask = (1 << bitsPerEntry) - 1;
        entriesPerStorageElement = StorageElementSize / bitsPerEntry;

        var storageSize = (entriesCount + entriesPerStorageElement - 1) / entriesPerStorageElement;
        storage = initializeStorage ? new long[storageSize] : GC.AllocateUninitializedArray<long>(storageSize);
    }

    private DataArray(long[] storage, int entriesCount, int bitsPerEntry, long entryMask, int entriesPerStorageElement)
    {
        this.storage = storage;
        this.entriesCount = entriesCount;
        this.bitsPerEntry = bitsPerEntry;
        this.entryMask = entryMask;
        this.entriesPerStorageElement = entriesPerStorageElement;
    }

    public int this[int index]
    {
        get
        {
            var storageIndex = index / entriesPerStorageElement;
            var element = storage[storageIndex];

            var offset = (index - storageIndex * entriesPerStorageElement) * bitsPerEntry;
            return (int)(element >> offset & entryMask);
        }

        set
        {
            var storageIndex = index / entriesPerStorageElement;
            var element = storage[storageIndex];

            var offset = (index - storageIndex * entriesPerStorageElement) * bitsPerEntry;
            element &= ~(entryMask << offset);
            storage[storageIndex] = element | (long)value << offset;
        }
    }

    public DataArray Grow(int minBitsPerEntry)
    {
        if (minBitsPerEntry <= bitsPerEntry)
            return this;

        var @new = new DataArray(minBitsPerEntry, entriesCount, false);
        for (var i = 0; i < entriesCount; i++)
            @new[i] = this[i];
        return @new;
    }

    public DataArray Clone()
    {
        var storageCopy = GC.AllocateUninitializedArray<long>(storage.Length);
        Array.Copy(storage, storageCopy, storage.Length);
        return new DataArray(storageCopy, entriesCount, bitsPerEntry, entryMask, entriesPerStorageElement);
    }
}
