using Obsidian.API.Inventory;
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
    private readonly Dictionary<BaseContainer, Revision> revisions = [];
    private readonly Dictionary<BaseContainer, ItemStack?[]> items = [];
    private readonly List<IDisposable> held = [];

    private readonly record struct Revision(long Items, long Transactions);

    // Call without transfer/chunk entity locks or an ambient snapshot. A virtual getter retains only its own
    // inventory lock and the caller's existing tick/generator scope, never another container's admission.
    internal static bool TryCapture(
        IEnumerable<BaseContainer> inventories,
        out InventorySnapshot? snapshot,
        out BaseContainer? busy)
    {
        var capture = new InventorySnapshot();
        snapshot = null;
        busy = null;
        foreach (var inventory in inventories.Distinct())
        {
            if (!inventory.TryLock(out var handle))
            {
                busy = inventory;
                return false;
            }

            using var captured = handle;
            var parts = inventory.LockParts.Distinct().ToArray();
            var before = parts.Select(part => new Revision(part.EngineRevision, part.TransactionVersion)).ToArray();
            var contents = new ItemStack?[inventory.Size];
            for (var slot = 0; slot < contents.Length; slot++)
            {
                var item = inventory.GetItem(slot);
                contents[slot] = item is null ? null : new ItemStack(item, item.Count);
            }

            for (var index = 0; index < parts.Length; index++)
            {
                var part = parts[index];
                var revision = before[index];
                var overlaps = capture.revisions.TryGetValue(part, out var earlier);
                // Shared parts may have only our own new capture transaction since their previous read.
                if (part.EngineRevision != revision.Items || part.TransactionVersion != revision.Transactions
                    || overlaps && (earlier.Items != revision.Items
                        || earlier.Transactions + 1 != revision.Transactions))
                {
                    busy = inventory;
                    return false;
                }

                capture.revisions[part] = revision;
            }

            capture.items.Add(inventory, contents);
        }

        snapshot = capture;
        return true;
    }

    internal static bool TryEnter(
        IEnumerable<BaseContainer> inventories,
        out InventorySnapshot? snapshot,
        out BaseContainer? busy)
    {
        if (!TryCapture(inventories, out snapshot, out busy))
            return false;

        return snapshot!.TryEnter(out busy);
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
                if (part.EngineRevision != revision.Items || part.TransactionVersion != revision.Transactions)
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

        this.previous = current;
        current = this;
        return true;
    }

    /// <summary>Used only by host snapshot serialization and retirement, never plugin Save overrides.</summary>
    internal static IDisposable Lock(BaseContainer inventory)
    {
        if (current is null)
            return inventory.Lock();

        if (!inventory.LockParts.All(current.revisions.ContainsKey))
            throw new InvalidOperationException("A snapshot reached an inventory it did not admit.");

        return Reused.Instance;
    }

    internal static ItemStack? GetItem(BaseContainer inventory, int slot) => current is null
        ? inventory.GetItem(slot)
        : current.items[inventory][slot];

    public void Dispose()
    {
        if (ReferenceEquals(current, this))
            current = this.previous;

        this.Release();
    }

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
