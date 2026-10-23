using Obsidian.API.Registries;
using Obsidian.API.World.Generator;

namespace Obsidian.API.World.Features;

/// <summary>
/// A set of blocks written as block ids and block tags (<c>#minecraft:logs</c>), like vanilla's HolderSet&lt;Block&gt;.
/// </summary>
/// <remarks>
/// Feature data writes these as a single string or a list; the source generator emits <c>new BlockSet(...)</c>, and
/// plugins' feature types can have properties of this type too. Tags are resolved the first time the set is used.
/// </remarks>
public sealed class BlockSet
{
    private readonly string[] entries;

    public BlockSet(params string[] entries) => this.entries = entries;

    public IReadOnlyList<string> Entries => this.entries;

    // Whether each block, by registry id, is in the set.
    private bool[] Members => field ??= this.Resolve();

    /// <summary>
    /// Whether the block's type (any state) is in the set.
    /// </summary>
    public bool Contains(IBlock block) => this.ContainsRegistryId(block.RegistryId);

    /// <summary>
    /// Whether the block with the registry id (any of its states) is in the set.
    /// </summary>
    public bool ContainsRegistryId(int registryId)
    {
        var members = this.Members;
        return (uint)registryId < (uint)members.Length && members[registryId];
    }

    private bool[] Resolve()
    {
        var ids = new HashSet<int>();

        foreach (var entry in this.entries)
        {
            if (entry.StartsWith('#'))
            {
                if (!TagsRegistry.TryGet("block", entry[1..], out var tag))
                    throw new InvalidOperationException($"Unknown block tag '{entry}'.");

                ids.UnionWith(tag.Entries);
            }
            else
            {
                ids.Add(new SimpleBlockState { Name = entry }.ToBlock().RegistryId);
            }
        }

        var members = new bool[ids.Count == 0 ? 0 : ids.Max() + 1];
        foreach (var id in ids)
            members[id] = true;

        return members;
    }
}
