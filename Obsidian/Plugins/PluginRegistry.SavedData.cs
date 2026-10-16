using Obsidian.API.Data;
using Obsidian.API.Inventory.DataComponents;
using System.Reflection;

namespace Obsidian.Plugins;

public sealed partial class PluginRegistry
{
    public SavedDataType<TValue> RegisterSavedData<TValue>(string identifier, ComponentCodec<TValue> codec)
    {
        var owner = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());
        return RegisterSavedData(owner, identifier, codec);
    }

    public AttachmentType<TValue> RegisterAttachment<TValue>(
        string identifier,
        ComponentCodec<TValue> codec,
        ComponentNetworkCodec<TValue>? networkCodec = null,
        bool copyOnDeath = false)
    {
        var owner = this.pluginManager.GetPluginContainerByAssembly(Assembly.GetCallingAssembly());
        return RegisterAttachment(owner, identifier, codec, networkCodec, copyOnDeath);
    }

    internal static SavedDataType<TValue> RegisterSavedData<TValue>(
        PluginContainer owner, string identifier, ComponentCodec<TValue> codec, bool client = false)
    {
        CheckDataIdentifier(owner, identifier);
        if (identifier.Split(':', 2)[1].Split('/').Any(segment =>
            segment.EndsWith(".dat", StringComparison.Ordinal)
            || segment.EndsWith(".dat.tmp", StringComparison.Ordinal)))
        {
            throw new ArgumentException($"{identifier} conflicts with saved data file suffixes.");
        }

        ArgumentNullException.ThrowIfNull(codec);
        var key = (client ? "client:saved:" : "saved:") + identifier;
        var binding = new PluginDataBinding(key, owner, () => owner.TryEnter());
        RegisterDataBinding(owner, binding);
        return new SavedDataType<TValue>(identifier, codec, binding);
    }

    internal static AttachmentType<TValue> RegisterAttachment<TValue>(
        PluginContainer owner,
        string identifier,
        ComponentCodec<TValue> codec,
        ComponentNetworkCodec<TValue>? networkCodec,
        bool copyOnDeath,
        bool client = false)
    {
        CheckDataIdentifier(owner, identifier);
        ArgumentNullException.ThrowIfNull(codec);
        var binding = new PluginDataBinding(
            (client ? "client:attachment:" : "attachment:") + identifier,
            owner,
            () => owner.TryEnter(),
            copyOnDeath,
            networkCodec is null ? null : (tag, writer) => networkCodec.Write(writer, codec.Decode(tag)),
            networkCodec is null ? null : reader => codec.Encode(networkCodec.Read(reader)),
            // The engine's item codec makes an item attachment, which it reads itself for item access and deaths.
            HoldsItems: ReferenceEquals(codec, ItemAttachments.Codec));
        RegisterDataBinding(owner, binding);
        return new AttachmentType<TValue>(identifier, codec, binding);
    }

    private static void RegisterDataBinding(PluginContainer owner, PluginDataBinding binding)
    {
        owner.ThrowIfClosing();
        PluginDataBindings.Bind(binding);
        owner.TrackAddition(() => PluginDataBindings.Release(binding));
    }

    private static void CheckDataIdentifier(PluginContainer owner, string identifier)
    {
        CheckIdentifier(owner, identifier);
        var parts = identifier.Split(':', 2);
        // Saved ids become paths on Windows too. Reject traversal, aliases and reserved device names.
        if (parts[0] == "obsidian"
            || !PluginTags.IsPortableName(parts[0])
            || !parts[1].Split('/').All(PluginTags.IsPortableName))
            throw new ArgumentException($"{identifier} isn't a portable plugin data identifier.");
    }
}
