using Obsidian.API.Blocks;
using Obsidian.API.Plugins;

namespace Obsidian.API.BlockEntities;

/// <summary>
/// A block entity type a plugin registered with <see cref="IPluginRegistry.RegisterBlockEntity{TBlockEntity}"/>, like
/// a NeoForge mod's <c>BlockEntityType</c>: the blocks that have one, and the class created for them.
/// </summary>
/// <remarks>
/// The server owns the type: it keeps its instance, identifier and id until the server stops, even when the plugin
/// reloads or unloads; a reload replaces its blocks and class.
/// </remarks>
public sealed class BlockEntityType
{
    internal BlockEntityType(int id, string identifier)
    {
        this.Id = id;
        this.Identifier = identifier;
    }

    /// <summary>
    /// The type's identifier, like <c>myplugin:counter</c>, which its block entities are saved with.
    /// </summary>
    public string Identifier { get; }

    /// <summary>
    /// The type's id in the block entity type registry. Vanilla's come first; plugins' follow, in the order they were
    /// first registered.
    /// </summary>
    public int Id { get; }

    /// <summary>
    /// The blocks that have a block entity of the type.
    /// </summary>
    public IReadOnlyList<BlockType> Blocks { get; internal set; } = [];

    /// <summary>
    /// The plugin that registered the type and provides its class, or null while the plugin is unloaded.
    /// </summary>
    public IPluginContainer? Owner => this.Binding?.Owner;

    // The plugin and how to create its block entities, replaced together when the plugin registers the type again,
    // and dropped when it unloads.
    internal BlockEntityBinding? Binding { get; set; }

    public override string ToString() => this.Identifier;
}

/// <summary>
/// The plugin that registered a block entity type, and what creates its block entities.
/// </summary>
internal sealed record BlockEntityBinding(IPluginContainer Owner, Func<BlockEntity> Factory);
