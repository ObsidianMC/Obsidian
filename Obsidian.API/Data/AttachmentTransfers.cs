using System.Threading;

namespace Obsidian.API.Data;

/// <summary>
/// Reservations for the item attachments a menu touches. They hold no lock across item access or plugin code.
/// Death stops new reservations and waits for existing clicks, including their rollback, before draining.
/// </summary>
internal sealed class AttachmentTransfers : IDisposable
{
    private static readonly AsyncLocal<AttachmentTransfers?> current = new();
    private readonly AttachmentTransfers? outer = current.Value;
    private readonly Lock gate = new();
    private readonly HashSet<AttachmentStorage> holders = [];
    private bool closed;

    internal AttachmentTransfers() => current.Value = this;

    internal bool Reserve(MenuContainer menu)
    {
        foreach (var access in menu.Slots.Select(slot => slot.Items).OfType<AttachmentItemAccess>())
        {
            if (this.Reserve(access.Storage) != true)
                return false;
        }

        return true;
    }

    // Enlist dynamically too: a plugin may wrap an attachment access in its own IItemAccess implementation.
    internal static bool? ReserveCurrent(AttachmentStorage storage) => current.Value?.Reserve(storage);

    private bool? Reserve(AttachmentStorage storage)
    {
        lock (this.gate)
        {
            if (this.closed)
                return null;

            if (this.holders.Contains(storage))
                return true;

            if (!storage.TryBeginTransfer())
                return false;

            this.holders.Add(storage);
            return true;
        }
    }

    public void Dispose()
    {
        current.Value = this.outer;
        AttachmentStorage[] reserved;

        lock (this.gate)
        {
            this.closed = true;
            reserved = [.. this.holders];
            this.holders.Clear();
        }

        foreach (var storage in reserved)
            storage.EndTransfer();
    }
}
