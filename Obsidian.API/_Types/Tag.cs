namespace Obsidian.API;

/// <summary>
/// A tag: a named group of a registry's entries, like the blocks a pickaxe mines (<c>minecraft:mineable/pickaxe</c>).
/// </summary>
/// <remarks>
/// Plugins add to vanilla's tags and make their own with tag files in their <c>data/</c> folder, like a data pack's,
/// or programmatically; vanilla's tags then hold the plugins' entries too.
/// </remarks>
public sealed record class Tag
{
    private ImmutableArray<int> entries;

    /// <summary>
    /// The tag's namespace: <c>minecraft</c> for vanilla's tags.
    /// </summary>
    public string Namespace { get; init; } = "minecraft";

    /// <summary>
    /// The tag's name, the last part of its path, like <c>pickaxe</c>.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// The tag's registry followed by the folders of its path, like <c>block/mineable</c>.
    /// </summary>
    public required string Type { get; init; }

    public bool Replace { get; init; }

    /// <summary>
    /// The ids of the tag's entries in its registry.
    /// </summary>
    public required ImmutableArray<int> Entries
    {
        get => this.entries;
        init => this.entries = value;
    }

    public int Count => this.Entries.Length;

    /// <summary>
    /// The registry the tag groups entries of, like <c>block</c> or <c>worldgen/biome</c>.
    /// </summary>
    public string Registry
    {
        get
        {
            var folder = this.Type.IndexOf('/');

            // Worldgen registries have a folder of their own, like worldgen/biome.
            if (folder >= 0 && this.Type.StartsWith("worldgen/", StringComparison.Ordinal))
                folder = this.Type.IndexOf('/', folder + 1);

            return folder < 0 ? this.Type : this.Type[..folder];
        }
    }

    /// <summary>
    /// The tag's path in its registry, like <c>mineable/pickaxe</c>.
    /// </summary>
    public string Path => this.Type.Length == this.Registry.Length
        ? this.Name
        : $"{this.Type[(this.Registry.Length + 1)..]}/{this.Name}";

    /// <summary>
    /// The tag's identifier, like <c>minecraft:mineable/pickaxe</c>.
    /// </summary>
    public string Identifier => $"{this.Namespace}:{this.Path}";

    // Plugins' tag files change a vanilla tag's entries in place, so that code holding the tag sees them.
    internal void SetEntries(ImmutableArray<int> value) => this.entries = value;
}
