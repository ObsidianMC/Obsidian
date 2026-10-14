using Obsidian.API.Inventory;
using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Obsidian.API.Data;

/// <summary>
/// An entity (players included) or chunk that can hold plugin data. Reads return detached values. Set and Remove
/// publish changes atomically and throw InvalidOperationException after the holder retires for removal or its
/// final save. Synchronize the source object yourself when setting it from outside its level tick.
/// </summary>
public interface IAttachmentHolder
{
}

/// <summary>Persistent attachments. Holders keep NBT only, including unknown and unloaded types.</summary>
public static class AttachmentExtensions
{
    public static bool TryGetAttachment<TValue>(
        this IAttachmentHolder holder, AttachmentType<TValue> type, [MaybeNullWhen(false)] out TValue value)
    {
        using var call = type.Binding.Enter();
        var saved = AttachmentStorage.For(holder).Get(type.Identifier);
        value = saved is null ? default : type.Codec.Decode(saved);
        return saved is not null;
    }

    public static TValue? GetAttachment<TValue>(this IAttachmentHolder holder, AttachmentType<TValue> type) =>
        holder.TryGetAttachment(type, out var value) ? value : default;

    public static void SetAttachment<TValue>(this IAttachmentHolder holder, AttachmentType<TValue> type, TValue value)
    {
        using var call = type.Binding.Enter();
        var snapshot = AttachmentStorage.CopyValue(type.Identifier, type.Codec.Encode(value));
        if (!PluginDataBindings.TryPublish(type.Binding,
            () => AttachmentStorage.For(holder).Set(type.Identifier, snapshot)))
        {
            throw new InvalidOperationException($"{type.Identifier} is unloaded or replaced.");
        }

        type.Binding.RaiseChanged(holder);
    }

    public static bool RemoveAttachment<TValue>(this IAttachmentHolder holder, AttachmentType<TValue> type)
    {
        using var call = type.Binding.Enter();
        var removed = false;
        if (!PluginDataBindings.TryPublish(type.Binding,
            () => removed = AttachmentStorage.For(holder).Remove(type.Identifier)))
        {
            throw new InvalidOperationException($"{type.Identifier} is unloaded or replaced.");
        }

        if (removed)
            type.Binding.RaiseChanged(holder);

        return removed;
    }
}

internal sealed class AttachmentStorage
{
    // The engine namespace avoids both vanilla tags and a misleading claim of NeoForge save compatibility.
    internal const string NbtKey = "obsidian:attachments";
    private static readonly ConditionalWeakTable<IAttachmentHolder, AttachmentStorage> holders = new();
    private readonly Lock gate = new();
    private NbtCompound saved = new(NbtKey);
    private long revision;
    private bool retired;
    private bool deathPending;
    private int transfers;
    private TaskCompletionSource? transfersFinished;

    // A reservation is a counter, not a held lock. Death never waits for a click while holding any engine lock.
    internal bool TryBeginTransfer()
    {
        lock (this.gate)
        {
            if (this.retired || this.deathPending)
                return false;

            this.transfers++;
            return true;
        }
    }

    internal void EndTransfer()
    {
        lock (this.gate)
        {
            if (--this.transfers == 0)
                this.transfersFinished?.TrySetResult();
        }
    }

    internal Task BeginDeath()
    {
        lock (this.gate)
        {
            this.deathPending = true;
            if (this.transfers == 0)
                return Task.CompletedTask;

            this.transfersFinished ??= new(TaskCreationOptions.RunContinuationsAsynchronously);
            return this.transfersFinished.Task;
        }
    }

    internal void EndDeath()
    {
        lock (this.gate)
        {
            this.deathPending = false;
            this.transfersFinished = null;
        }
    }

    // Retirement and publication share this gate. Once retired, the final snapshot cannot miss a successful write.
    internal void Retire()
    {
        lock (this.gate)
            this.retired = true;
    }

    private void ThrowIfRetired()
    {
        if (this.retired)
            throw new InvalidOperationException("The attachment holder has been retired.");
    }

    internal long Revision
    {
        get
        {
            lock (this.gate)
                return this.revision;
        }
    }

    internal static AttachmentStorage For(IAttachmentHolder holder) =>
        holders.GetValue(holder, static _ => new AttachmentStorage());

    internal INbtTag? Get(string identifier)
    {
        lock (this.gate)
            return Copy(this.saved).TryGetTag(identifier, out var value) ? value : null;
    }

    internal void Set(string identifier, INbtTag value)
    {
        var copy = CopyValue(identifier, value);
        lock (this.gate)
        {
            this.ThrowIfRetired();
            this.saved.Remove(identifier);
            this.saved.Add(copy);
            this.revision++;
        }
    }

    internal bool Remove(string identifier)
    {
        lock (this.gate)
        {
            this.ThrowIfRetired();
            if (!this.saved.Remove(identifier))
                return false;

            this.revision++;
            return true;
        }
    }

    internal NbtCompound Snapshot()
    {
        lock (this.gate)
            return Copy(this.saved);
    }

    internal void Load(NbtCompound holder)
    {
        lock (this.gate)
        {
            this.saved = holder.TryGetTag<NbtCompound>(NbtKey, out var data) ? Copy(data) : new(NbtKey);
            this.revision++;
        }
    }

    internal void Save(NbtCompound holder)
    {
        holder.Remove(NbtKey);
        holder.Add(this.Snapshot());
    }

    internal void Respawn(IAttachmentHolder holder)
    {
        foreach (var (identifier, _) in this.Snapshot())
        {
            var binding = PluginDataBindings.FindAttachment(identifier);
            var removed = false;

            // Unknown types stay untouched. A missing plugin must not destroy its player's saved data. Items were
            // dropped as the player died already, or kept (see TakeDeathDrops).
            if (binding is { CopyOnDeath: false, HoldsItems: false })
                PluginDataBindings.TryPublish(binding, () => removed = this.Remove(identifier));

            if (removed)
                binding!.NotifyChanged(holder);
        }
    }

    /// <summary>
    /// Takes the items of the holder's item attachments (see <see cref="ItemAttachments"/>) that a death drops: those
    /// of loaded types without <c>CopyOnDeath</c>, removing them. The caller drops them, when vanilla's
    /// <c>keep_inventory</c> rule is off; with it on, or with <c>CopyOnDeath</c>, they're kept like the inventory.
    /// Call only after <see cref="BeginDeath"/> completes, without any window locks held.
    /// </summary>
    internal List<ItemStack> TakeDeathDrops(IAttachmentHolder holder)
    {
        var drops = new List<ItemStack>();

        foreach (var (identifier, _) in this.Snapshot())
        {
            if (PluginDataBindings.FindAttachment(identifier) is not { CopyOnDeath: false, HoldsItems: true } binding)
                continue;

            INbtTag? taken = null;
            PluginDataBindings.TryPublish(binding, () => taken = this.TryTake(identifier));

            if (taken is null)
                continue;

            drops.AddRange(ItemAttachments.Decode(taken).OfType<ItemStack>());
            binding.NotifyChanged(holder);
        }

        return drops;
    }

    // Removes and returns a value, or null when there's none or the holder is retired.
    private INbtTag? TryTake(string identifier)
    {
        lock (this.gate)
        {
            if (this.retired || !this.saved.TryGetTag(identifier, out var value))
                return null;

            this.saved.Remove(identifier);
            this.revision++;
            return value;
        }
    }

    /// <summary>
    /// Changes a value in one step, under the holder's lock: <paramref name="change"/> gets the saved value (null when
    /// there's none) and returns its replacement, or null to leave it. It's host code, never a plugin's codec.
    /// </summary>
    /// <returns>False when the holder is retired, and nothing changed.</returns>
    internal bool TryUpdate(string identifier, Func<INbtTag?, INbtTag?> change)
    {
        lock (this.gate)
        {
            if (this.retired)
                return false;

            var current = this.saved.TryGetTag(identifier, out var value) ? value : null;
            if (change(current) is not INbtTag replacement)
                return true;

            var copy = CopyValue(identifier, replacement);
            this.saved.Remove(identifier);
            this.saved.Add(copy);
            this.revision++;
            return true;
        }
    }

    internal static NbtCompound Copy(NbtCompound compound) => ComponentCopies.Nbt(compound)!;

    internal static INbtTag CopyValue(string identifier, INbtTag value)
    {
        var parent = value.Parent;
        var name = value.Name;
        try
        {
            var wrapper = new NbtCompound();
            wrapper.Add(identifier, value);
            var copy = Copy(wrapper)[identifier];
            copy.Parent = null;
            return copy;
        }
        finally
        {
            value.Parent = parent;
            value.Name = name;
        }
    }
}
