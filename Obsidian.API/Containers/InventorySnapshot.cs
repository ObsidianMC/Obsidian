using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.API.Utilities;
using Obsidian.Nbt;
using System.Threading;

namespace Obsidian.API;

/// <summary>
/// Captures virtual inventory reads before host admission, then tries every captured inventory together.
/// Only host serializers run in the admitted scope; ordinary inventory locks remain non-reentrant.
/// </summary>
internal sealed class InventorySnapshot : IDisposable
{
    [ThreadStatic] private static InventorySnapshot? current;

    private InventorySnapshot? previous;
    private bool preparing;
    private readonly Dictionary<ItemStack, (long Version, NbtCompound Data)> itemData =
        new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BaseContainer, long> revisions = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<BaseContainer, NbtCompound[]> items = new(ReferenceEqualityComparer.Instance);
    private readonly List<IDisposable> held = [];
    private readonly List<(BaseContainer Inventory, ItemStack Stack, long Version)> sources = [];

    // Call without transfer/chunk entity locks or an admitted snapshot. Preparation holds only the current
    // inventory and the caller's existing tick/generator scope; auxiliary host reads must try, never wait.
    internal static bool TryCapture(
        IEnumerable<BaseContainer> inventories,
        out InventorySnapshot? snapshot,
        out BaseContainer? busy)
    {
        var capture = new InventorySnapshot();
        snapshot = null;
        busy = null;
        using var preparation = capture.Prepare();
        try
        {
            foreach (var inventory in inventories.Distinct<BaseContainer>(ReferenceEqualityComparer.Instance))
            {
                if (!inventory.TryLock(out var handle))
                {
                    busy = inventory;
                    return false;
                }

                using var captured = handle;
                var parts = inventory.LockParts.Distinct<BaseContainer>(ReferenceEqualityComparer.Instance).ToArray();
                var before = parts.Select(part => part.EngineRevision).ToArray();
                var contents = new List<NbtCompound>();
                for (var slot = 0; slot < inventory.Size; slot++)
                {
                    var item = inventory.GetItem(slot);
                    if (item is null)
                        continue;

                    capture.sources.Add((inventory, item, item.Version));
                    if (item.IsAir || item.Count <= 0)
                        continue;

                    // Value getters and nested item conversion can run plugin copy delegates. Finish all of it
                    // while only this inventory is owned, before transfer or ambient host admission.
                    var saved = item.ToNbt();
                    saved.Add(new NbtTag<byte>("Slot", (byte)slot));
                    contents.Add(ComponentCopies.Nbt(saved)!);
                }

                for (var index = 0; index < parts.Length; index++)
                {
                    var part = parts[index];
                    var revision = before[index];
                    if (part.EngineRevision != revision
                        || capture.revisions.TryGetValue(part, out var earlier) && earlier != revision)
                    {
                        busy = inventory;
                        return false;
                    }

                    capture.revisions[part] = revision;
                }

                capture.items.Add(inventory, contents.ToArray());
            }

            snapshot = capture;
            return true;
        }
        catch (BusyException blocked)
        {
            busy = blocked.Inventory;
            return false;
        }
    }

    // Additional block/entity stacks (records, dropped items, projectiles) are encoded without transfer or
    // admission. Known inventories read their prepared NBT; an auxiliary host save can only try another lock.
    internal InventorySnapshot Prepare()
    {
        this.preparing = true;
        this.previous = current;
        current = this;
        return this;
    }

    internal static bool TryGetItemData(ItemStack item, string name, out NbtCompound? data)
    {
        data = null;
        if (current is null)
            return false;

        if (current.itemData.TryGetValue(item, out var saved) && saved.Version == item.Version)
        {
            data = ComponentCopies.Nbt(saved.Data)!;
            data.Name = name;
            return true;
        }

        if (current.preparing)
            return false;

        throw new ChangedException();
    }

    internal static void RecordItemData(ItemStack item, long version, NbtCompound data)
    {
        if (current is { preparing: true })
            current.itemData[item] = (version, ComponentCopies.Nbt(data)!);
    }

    internal bool TryEnter(out BaseContainer? busy)
    {
        busy = null;
        try
        {
            foreach (var (part, revision) in this.revisions)
            {
                if (!part.TransactionLock.Wait(0))
                {
                    this.Release();
                    busy = part;
                    return false;
                }

                this.held.Add(new LockRelease(part.TransactionLock));
                if (part.EngineRevision != revision)
                {
                    this.Release();
                    busy = part;
                    return false;
                }
            }
        }
        catch
        {
            this.Release();
            throw;
        }

        // A plugin getter may return a stack outside the base slots. Its nonvirtual item version still
        // detects in-place count/component changes without calling plugin code under transfer/admission.
        foreach (var source in this.sources)
        {
            if (source.Stack.Version == source.Version)
                continue;

            this.Release();
            busy = source.Inventory;
            return false;
        }

        foreach (var (stack, saved) in this.itemData)
        {
            if (stack.Version == saved.Version)
                continue;

            this.Release();
            return false;
        }

        this.previous = current;
        current = this;
        return true;
    }

    /// <summary>Used only by host snapshot serialization and retirement, never plugin Save overrides.</summary>
    internal static IDisposable Lock(BaseContainer inventory)
    {
        if (current is null)
            return inventory.Lock();

        if (current.preparing)
        {
            if (current.items.ContainsKey(inventory))
                return Reused.Instance;

            if (!inventory.TryLock(out var handle))
                throw new BusyException(inventory);

            return handle;
        }

        if (!inventory.LockParts.All(current.revisions.ContainsKey))
            throw new InvalidOperationException("A snapshot reached an inventory it did not admit.");

        return Reused.Instance;
    }

    // The admitted path only copies host-owned NBT; it must never reach component Value/copy delegates.
    internal static NbtList SaveItems(BaseContainer inventory)
    {
        var saved = new NbtList(NbtTagType.Compound, "Items");
        if (current is not null && current.items.TryGetValue(inventory, out var prepared))
        {
            foreach (var item in prepared)
                saved.Add(ComponentCopies.Nbt(item)!);

            return saved;
        }

        if (current is { preparing: false })
            throw new ChangedException();

        for (var slot = 0; slot < inventory.Size; slot++)
        {
            var item = inventory.GetItem(slot);
            if (item is null || item.IsAir || item.Count <= 0)
                continue;

            var tag = item.ToNbt();
            tag.Add(new NbtTag<byte>("Slot", (byte)slot));
            saved.Add(tag);
        }

        return saved;
    }

    public void Dispose()
    {
        if (ReferenceEquals(current, this))
            current = this.previous;

        this.preparing = false;
        this.Release();
    }

    internal sealed class BusyException(BaseContainer inventory) : Exception("A snapshot inventory is busy.")
    {
        internal BaseContainer Inventory => inventory;
    }

    internal sealed class ChangedException() : Exception("An item changed after snapshot preparation.");

    private void Release()
    {
        for (var index = this.held.Count - 1; index >= 0; index--)
            this.held[index].Dispose();

        this.held.Clear();
    }

    private sealed class Reused : IDisposable
    {
        internal static readonly Reused Instance = new();

        public void Dispose()
        {
        }
    }
}
