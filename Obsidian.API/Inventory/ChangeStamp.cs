using System.Threading;

namespace Obsidian.API.Inventory;

/// <summary>
/// Hands out stamps that grow across the whole server, one per change to a stack or a container's slots, so that a
/// container's <see cref="BaseContainer.Revision"/>, the latest stamp among its slots and stacks, grows whenever any
/// of them changes.
/// </summary>
internal static class ChangeStamp
{
    private static long last;

    public static long Next() => Interlocked.Increment(ref last);
}
