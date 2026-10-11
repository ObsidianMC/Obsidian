using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Registries;

/// <summary>
/// Names a tag by its registry and identifier, like Fabric's and NeoForge's tag keys; looking it up gives the tag as
/// it is now, with what plugins added to it.
/// </summary>
/// <param name="Registry">The tag's registry, like <c>block</c>.</param>
/// <param name="Identifier">The tag's identifier, like <c>c:ores</c>.</param>
public readonly record struct TagKey(string Registry, string Identifier)
{
    /// <summary>
    /// Whether the registry entry with the given id is in the tag; false while the tag doesn't exist.
    /// </summary>
    public bool Contains(int id) => TagsRegistry.Contains(this.Registry, this.Identifier, id);

    /// <summary>
    /// Whether a block is in the tag, for a block tag.
    /// </summary>
    public bool Contains(IBlock block) => this.Contains(block.RegistryId);

    /// <summary>
    /// Finds the tag.
    /// </summary>
    public bool TryGet([NotNullWhen(true)] out Tag? tag) =>
        TagsRegistry.TryGet(this.Registry, this.Identifier, out tag);
}
