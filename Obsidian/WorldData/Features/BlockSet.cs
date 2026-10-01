using System.Collections.Frozen;

namespace Obsidian.WorldData.Features;

/// <summary>
/// A set of blocks written as block ids and block tags (<c>#minecraft:logs</c>), like vanilla's HolderSet&lt;Block&gt;.
/// </summary>
/// <remarks>
/// Feature data writes these as a single string or a list; the source generator emits <c>new BlockSet(...)</c>.
/// </remarks>
public sealed class BlockSet
{
    private readonly string[] entries;

    public BlockSet(params string[] entries) => this.entries = entries;

    public IReadOnlyList<string> Entries => this.entries;

    private FrozenSet<int> RegistryIds => field ??= this.Resolve();

    /// <summary>
    /// Whether the block's type (any state) is in the set.
    /// </summary>
    public bool Contains(IBlock block) => this.RegistryIds.Contains(block.RegistryId);

    private FrozenSet<int> Resolve()
    {
        var ids = new HashSet<int>();

        foreach (var entry in this.entries)
        {
            if (entry.StartsWith('#'))
            {
                var name = entry[1..].Replace("minecraft:", string.Empty);
                var tag = TagsRegistry.Block.All.FirstOrDefault(tag => tag.Name == name)
                    ?? throw new InvalidOperationException($"Unknown block tag '{entry}'.");

                ids.UnionWith(tag.Entries);
            }
            else
            {
                ids.Add(BlocksRegistry.Get(entry).RegistryId);
            }
        }

        return ids.ToFrozenSet();
    }
}
