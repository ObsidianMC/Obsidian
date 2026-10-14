using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.Plugins;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Registries;

/// <summary>
/// The block entity types: vanilla's, by their identifiers, and those plugins registered after them, whose ids follow
/// vanilla's in registration order.
/// </summary>
internal static class BlockEntityTypesRegistry
{
    private static readonly Lock registering = new();

    private static readonly Lazy<FrozenDictionary<string, int>> vanillaIds = new(() => VanillaIdentifiers
        .Select((identifier, id) => (identifier, id))
        .ToFrozenDictionary(entry => entry.identifier, entry => entry.id));

    // Registering replaces the tables whole, so lookups never lock.
    private static BlockEntityType[] types = [];
    private static FrozenDictionary<string, BlockEntityType> byName =
        FrozenDictionary<string, BlockEntityType>.Empty;
    private static FrozenDictionary<int, BlockEntityType> byBlock = FrozenDictionary<int, BlockEntityType>.Empty;

    private static int revision;

    /// <summary>
    /// Vanilla's block entity types' identifiers, indexed by id.
    /// </summary>
    public static IReadOnlyList<string> VanillaIdentifiers => VanillaRegistryIds.Get("minecraft:block_entity_type");

    /// <summary>
    /// The block entity types plugins registered, in id order.
    /// </summary>
    public static IReadOnlyList<BlockEntityType> PluginTypes => types;

    /// <summary>
    /// Grows whenever a plugin's type is bound to its plugin, so that levels know to create the block entities of
    /// types kept as saved while their plugin was gone.
    /// </summary>
    public static int Revision => Volatile.Read(ref revision);

    /// <summary>
    /// Finds a plugin's block entity type by its identifier, ignoring case.
    /// </summary>
    public static bool TryGet(string identifier, [NotNullWhen(true)] out BlockEntityType? type) =>
        byName.TryGetValue(identifier, out type);

    /// <summary>
    /// The block entity type of a plugin's block, or null when it has none.
    /// </summary>
    public static BlockEntityType? Of(BlockType block) => byBlock.GetValueOrDefault(block.Id);

    /// <summary>
    /// The network id of a block entity type, vanilla's or a plugin's, or -1 when it's unknown.
    /// </summary>
    public static int NetworkId(string identifier)
    {
        if (vanillaIds.Value.TryGetValue(identifier, out var id))
            return id;

        return byName.TryGetValue(identifier, out var type) ? type.Id : -1;
    }

    /// <summary>
    /// Registers a block entity type for <paramref name="blocks"/>, or returns the one registered before under the
    /// same identifier, which keeps its id and now covers <paramref name="blocks"/> instead of its old blocks.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The identifier is in vanilla's namespace, or one of the blocks has another type.
    /// </exception>
    public static BlockEntityType Register(string identifier, IReadOnlyList<BlockType> blocks)
    {
        if (identifier.StartsWith("minecraft:", StringComparison.Ordinal))
            throw new ArgumentException($"{identifier} is in vanilla's namespace.", nameof(identifier));

        lock (registering)
        {
            foreach (var block in blocks)
            {
                if (byBlock.TryGetValue(block.Id, out var other)
                    && !string.Equals(other.Identifier, identifier, StringComparison.OrdinalIgnoreCase))
                {
                    throw new ArgumentException($"{block} already has block entity type {other}.", nameof(blocks));
                }
            }

            if (!byName.TryGetValue(identifier, out var type))
            {
                type = new BlockEntityType(VanillaIdentifiers.Count + types.Length, identifier);
                API.Inventory.DataComponents.ComponentRegistryNames.Register(
                    "minecraft:block_entity_type", type.Id, identifier);
                types = [.. types, type];
                byName = types.ToFrozenDictionary(entry => entry.Identifier, StringComparer.OrdinalIgnoreCase);
            }

            type.Blocks = [.. blocks.Distinct()];
            byBlock = types
                .SelectMany(entry => entry.Blocks.Select(block => (block.Id, Type: entry)))
                .ToFrozenDictionary(entry => entry.Id, entry => entry.Type);

            return type;
        }
    }

    /// <summary>
    /// Gives a block entity type its plugin's class, unless the plugin started unloading: then its types were
    /// released already, and binding one now would keep the plugin loaded.
    /// </summary>
    /// <returns>Whether the type was bound.</returns>
    public static bool TryBind(
        BlockEntityType type,
        Func<BlockEntity> factory,
        IPluginContainer plugin,
        Func<bool> closing)
    {
        lock (registering)
        {
            if (closing())
                return false;

            type.Binding = new BlockEntityBinding(plugin, factory);
            Interlocked.Increment(ref revision);
            return true;
        }
    }

    /// <summary>
    /// Drops the class of every block entity type <paramref name="plugin"/> registered, as the plugin unloads. The
    /// types stay registered, with their ids and blocks, for their block entities saved in the world.
    /// </summary>
    public static void ReleasePlugin(IPluginContainer plugin)
    {
        lock (registering)
        {
            foreach (var type in types)
            {
                if (type.Binding?.Owner == plugin)
                    type.Binding = null;
            }
        }
    }
}
