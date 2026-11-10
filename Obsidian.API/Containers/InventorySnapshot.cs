using System.Threading;

namespace Obsidian.API;

/// <summary>
/// Admits all inventories of a synchronous host snapshot without waiting. Host serializers reuse these locks;
/// no plugin callbacks or awaits may run in this scope. Ordinary inventory locks remain non-reentrant.
/// </summary>
internal sealed class InventorySnapshot : IDisposable
{
    [ThreadStatic] private static InventorySnapshot? current;

    private readonly InventorySnapshot? previous = current;
    private readonly HashSet<BaseContainer> parts = [];
    private readonly List<IDisposable> held = [];

    internal static bool TryEnter(
        IEnumerable<BaseContainer> inventories,
        out InventorySnapshot? snapshot,
        out BaseContainer? busy)
    {
        var scope = new InventorySnapshot();
        try
        {
            foreach (var inventory in inventories)
            {
                foreach (var part in inventory.LockParts)
                {
                    if (!scope.parts.Add(part))
                        continue;

                    if (!part.TransactionLock.Wait(0))
                    {
                        scope.Release();
                        snapshot = null;
                        busy = part;
                        return false;
                    }

                    scope.held.Add(new LockRelease(part.TransactionLock));
                }
            }
        }
        catch
        {
            scope.Release();
            throw;
        }

        current = scope;
        snapshot = scope;
        busy = null;
        return true;
    }

    /// <summary>Used only by host snapshot serialization and retirement, never plugin Save overrides.</summary>
    internal static IDisposable Lock(BaseContainer inventory)
    {
        if (current is null)
            return inventory.Lock();

        if (!inventory.LockParts.All(current.parts.Contains))
            throw new InvalidOperationException("A snapshot reached an inventory it did not admit.");

        return Reused.Instance;
    }

    public void Dispose()
    {
        current = this.previous;
        this.Release();
    }

    private void Release()
    {
        for (var index = this.held.Count - 1; index >= 0; index--)
            this.held[index].Dispose();
    }

    private sealed class Reused : IDisposable
    {
        internal static readonly Reused Instance = new();

        public void Dispose()
        {
        }
    }
}
