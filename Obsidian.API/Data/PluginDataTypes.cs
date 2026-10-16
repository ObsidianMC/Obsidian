using Obsidian.API.Inventory.DataComponents;
using Obsidian.Nbt;

namespace Obsidian.API.Data;

/// <summary>The lifetime scope of a saved data instance.</summary>
public enum SavedDataScope
{
    Level,
    World
}

/// <summary>A saved data registration. Register once during ConfigureRegistry; handles expire on unload.</summary>
public sealed class SavedDataType<TValue>
{
    internal SavedDataType(string identifier, ComponentCodec<TValue> codec, PluginDataBinding binding)
    {
        this.Identifier = identifier;
        this.Codec = codec;
        this.Binding = binding;
    }

    public string Identifier { get; }
    internal ComponentCodec<TValue> Codec { get; }
    internal PluginDataBinding Binding { get; }
}

/// <summary>
/// A live saved value. Access Value on its owning level's tick, or with your own synchronization. MarkDirty publishes
/// a detached saved snapshot; world saves never call plugin code. Never retain this handle across plugin reloads.
/// </summary>
public sealed class SavedData<TValue>
{
    private readonly Func<IDisposable> enter;
    private readonly Action<TValue> publish;
    private readonly TValue value;

    internal SavedData(TValue value, Func<IDisposable> enter, Action<TValue> publish)
    {
        this.value = value;
        this.enter = enter;
        this.publish = publish;
    }

    /// <summary>The live plugin object. Mutating it requires MarkDirty before the next save.</summary>
    public TValue Value
    {
        get
        {
            using var call = this.enter();
            return this.value;
        }
    }

    /// <summary>
    /// Encodes the current value now, marking it for the next save. Throws after unload or replacement.
    /// </summary>
    public void MarkDirty()
    {
        using var call = this.enter();
        this.publish(this.value);
    }
}

/// <summary>
/// A player, entity or chunk attachment registration. Values are detached: mutate a Get result and Set it again.
/// Codecs run under the plugin call guard; they must not block or mutate shared plugin state.
/// </summary>
public sealed class AttachmentType<TValue>
{
    internal AttachmentType(string identifier, ComponentCodec<TValue> codec, PluginDataBinding binding)
    {
        this.Identifier = identifier;
        this.Codec = codec;
        this.Binding = binding;
    }

    public string Identifier { get; }
    public bool CopyOnDeath => this.Binding.CopyOnDeath;

    /// <summary>
    /// Raised after a holder's value of this type changes: set or removed by the plugin, changed through
    /// <see cref="ItemAttachments.GetItemAccess"/> (a menu click included), dropped at a death or removed at a death's
    /// respawn. Not raised as holders load. Handlers run synchronously inside a call into the plugin, possibly with a
    /// menu's locks held, so they must not wait; read the new value with a get. An exception from a handler reaches
    /// the plugin's own <c>SetAttachment</c> or <c>RemoveAttachment</c> caller, and is logged for the engine's
    /// changes. Handlers go with the registration on unload.
    /// </summary>
    public event Action<IAttachmentHolder>? Changed
    {
        add => this.Binding.Changed += value;
        remove => this.Binding.Changed -= value;
    }

    internal ComponentCodec<TValue> Codec { get; }
    internal PluginDataBinding Binding { get; }
}
