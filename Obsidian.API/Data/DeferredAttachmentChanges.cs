using System.Threading;

namespace Obsidian.API.Data;

/// <summary>Collects attachment notifications while engine transactions hold their locks.</summary>
internal sealed class DeferredAttachmentChanges : IDisposable
{
    private static readonly AsyncLocal<DeferredAttachmentChanges?> current = new();
    private readonly DeferredAttachmentChanges? outer = current.Value;
    private readonly HashSet<(PluginDataBinding Binding, IAttachmentHolder Holder)> changes = [];
    private bool closed;

    internal DeferredAttachmentChanges() => current.Value = this;

    internal static bool TryQueue(PluginDataBinding binding, IAttachmentHolder holder)
    {
        if (current.Value is not DeferredAttachmentChanges pending)
            return false;

        lock (pending.changes)
        {
            if (pending.closed)
                return false;

            pending.changes.Add((binding, holder));
        }

        return true;
    }

    // Declare outside the lock's using block: disposal delivers only the final, committed values, without locks.
    public void Dispose()
    {
        current.Value = this.outer;
        (PluginDataBinding Binding, IAttachmentHolder Holder)[] queued;

        lock (this.changes)
        {
            this.closed = true;
            queued = [.. this.changes];
            this.changes.Clear();
        }

        foreach (var (binding, holder) in queued)
            binding.NotifyChanged(holder);
    }
}
