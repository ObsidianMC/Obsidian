using System.Diagnostics;

namespace Obsidian.API.Utilities;
// Utility storage that can store multiple elements in 64-bit storage entries
// This type's storage must correspond to minecraft's compacted data array
internal sealed class DataArray
{
    // The following are spans are used to optimize (~4 times faster) out division,
    // replacing (index / entriesPerStorageElement) with (index * multiplier >> shifter).
    // Multiplier and shifter should be selected with index [bitsPerEntry]
    // This trick may yield incorrect results when "dividing" numbers out of range 0..4096,
    // but DataArray shouldn't be used for that many elements
    private static ReadOnlySpan<int> Multipliers => [0, 1, 1, 3121, 1, 2731, 3277, 3641, 1, 2341, 2731, 3277, 3277, 1, 1, 1, 1, 2731, 2731, 2731, 2731, 2731, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1];
    private static ReadOnlySpan<int> Shifts => [0, 6, 5, 16, 4, 15, 15, 15, 3, 14, 14, 14, 14, 2, 2, 2, 2, 13, 13, 13, 13, 13, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];

    public int BitsPerEntry => bitsPerEntry;
    public int Length => entriesCount;

    internal long[] storage;

    private readonly int bitsPerEntry;
    private readonly int entriesCount;

    private readonly long entryMask;
    private readonly int entriesPerStorageElement;

    private readonly int multiplier;
    private readonly int shift;

    public DataArray(int bitsPerEntry, int entriesCount)
    {
        const int StorageElementSize = 64; // Bits of long (backing type of storage)

        Debug.Assert(bitsPerEntry is > 0 and <= StorageElementSize);
        Debug.Assert(entriesCount >= 0);
        Debug.Assert(StorageElementSize / bitsPerEntry <= 4096); // See comments for Multipliers and Shifts

        this.bitsPerEntry = bitsPerEntry;
        this.entriesCount = entriesCount;

        entryMask = (1 << bitsPerEntry) - 1;
        entriesPerStorageElement = StorageElementSize / bitsPerEntry;

        multiplier = Multipliers[bitsPerEntry];
        shift = Shifts[bitsPerEntry];

        var storageSize = (entriesCount + entriesPerStorageElement - 1) / entriesPerStorageElement;
        storage = new long[storageSize];
    }

    private DataArray(long[] storage, int entriesCount, int bitsPerEntry, long entryMask, int entriesPerStorageElement)
    {
        this.storage = storage;
        this.entriesCount = entriesCount;
        this.bitsPerEntry = bitsPerEntry;
        this.entryMask = entryMask;
        this.entriesPerStorageElement = entriesPerStorageElement;
        multiplier = Multipliers[bitsPerEntry];
        shift = Shifts[bitsPerEntry];
    }

    public int this[int index]
    {
        get
        {
            var storageIndex = index * multiplier >> shift;
            var element = storage[storageIndex];

            var offset = (index - storageIndex * entriesPerStorageElement) * bitsPerEntry;
            return (int)(element >> offset & entryMask);
        }

        set
        {
            var storageIndex = index * multiplier >> shift;
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

        var @new = new DataArray(minBitsPerEntry, entriesCount);
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
