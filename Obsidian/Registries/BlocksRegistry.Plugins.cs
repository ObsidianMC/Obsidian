using Obsidian.API.Blocks;
using Obsidian.API.Inventory;
using Obsidian.API.Registries;
using Obsidian.API.Plugins;
using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Threading;

namespace Obsidian.Registries;

// The blocks plugins registered, after vanilla's: their ids and states' ids follow vanilla's, in registration order.
internal static partial class BlocksRegistry
{
    private static readonly Lock registering = new();
    private static readonly HashSet<BlockType> configuredBlocks = [];

    // Registering replaces the table whole, so lookups never lock. Set by the static constructor, which runs after the
    // generated tables are initialized.
    private static PluginBlockTable plugins;

    /// <summary>
    /// How many block states there are, vanilla's and plugins'.
    /// </summary>
    public static int StateCount => plugins.StateCount;

    /// <summary>
    /// The bits per entry of the global block palette, which clients work out from the number of states like this.
    /// </summary>
    public static int GlobalBitsPerBlocks => plugins.GlobalBits;

    /// <summary>
    /// The blocks plugins registered, in id order. They stay registered until the server stops.
    /// </summary>
    public static IReadOnlyList<BlockType> PluginBlocks => plugins.Types;

    /// <summary>
    /// Finds the plugin block a state id belongs to.
    /// </summary>
    public static bool TryGetPluginBlock(int stateId, [NotNullWhen(true)] out BlockType? type)
    {
        var table = plugins;
        type = null;

        if (stateId < StateToNumeric.Length || stateId >= table.StateCount)
            return false;

        // The types are in state id order, so the last one starting at or before the state holds it.
        var low = 0;
        var high = table.Types.Length - 1;

        while (low < high)
        {
            var middle = (low + high + 1) / 2;

            if (table.Types[middle].FirstStateId <= stateId)
                low = middle;
            else
                high = middle - 1;
        }

        type = table.Types[low];
        return true;
    }

    /// <summary>
    /// Finds a plugin block by its identifier, ignoring case.
    /// </summary>
    public static bool TryGetPluginBlock(string identifier, [NotNullWhen(true)] out BlockType? type) =>
        plugins.ByName.TryGetValue(identifier, out type);

    /// <summary>
    /// The item a block drops as itself: the vanilla item named like a vanilla block, or a plugin block's item.
    /// </summary>
    public static bool TryGetItem(IBlock block, [NotNullWhen(true)] out Item? item)
    {
        if (TryGetPluginBlock(block.GetHashCode(), out var type))
        {
            item = type.Item;
            return item is not null;
        }

        return ItemsRegistry.TryGet(block.UnlocalizedName, out item) && item != ItemsRegistry.Air;
    }

    /// <summary>
    /// The block an item places as itself: the vanilla block named like a vanilla item, or a plugin item's block.
    /// Items placing a block named otherwise (wheat seeds placing wheat, say) aren't covered.
    /// </summary>
    public static bool TryGetBlock(Item item, [NotNullWhen(true)] out IBlock? block)
    {
        if (item.Block is BlockType type)
        {
            block = type.DefaultState;
            return true;
        }

        // A plugin's item places only the block it's given, which a reload may take away.
        block = null;
        if (item.Id >= ItemsRegistry.VanillaCount || !resourceIdToName.TryGetValue(item.UnlocalizedName, out var name))
            return false;

        block = defaultBlockCache[name];
        return !block.IsAir;
    }

    /// <summary>
    /// The default state of the block with the given identifier, like <c>minecraft:stone</c>, vanilla's or a plugin's.
    /// </summary>
    public static bool TryGetDefault(string identifier, [NotNullWhen(true)] out IBlock? block)
    {
        if (resourceIdToName.TryGetValue(identifier, out var name))
        {
            block = defaultBlockCache[name];
            return true;
        }

        block = TryGetPluginBlock(identifier, out var type) ? type.DefaultState : null;
        return block is not null;
    }

    /// <summary>
    /// Registers a block, or returns the one registered before under the same identifier, which keeps its ids.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The identifier is one of vanilla's, or the block's state properties differ from those it was registered with.
    /// </exception>
    internal static BlockType Register(string identifier, BlockProperties properties)
    {
        lock (registering)
        {
            if (resourceIdToName.ContainsKey(identifier))
                throw new ArgumentException($"{identifier} is a vanilla block.", nameof(identifier));

            var table = plugins;
            var candidate = new BlockType(
                Names.Length + table.Types.Length,
                table.StateCount,
                identifier,
                properties.StateProperties,
                properties.DefaultState);

            if (!table.ByName.TryGetValue(identifier, out var existing))
            {
                plugins = new PluginBlockTable([.. table.Types, candidate], table.StateCount + candidate.StateCount);
                API.Inventory.DataComponents.ComponentRegistryNames.Register(
                    "minecraft:block", candidate.Id, identifier);
                return candidate;
            }

            // Clients number the block's states by its properties, which they learned when they joined.
            var same = existing.Properties.Length == candidate.Properties.Length
                && existing.Properties.Zip(candidate.Properties).All(pair => pair.First.Name == pair.Second.Name
                    && pair.First.Values.SequenceEqual(pair.Second.Values))
                && existing.DefaultStateId - existing.FirstStateId == candidate.DefaultStateId - candidate.FirstStateId;

            return same
                ? existing
                : throw new ArgumentException(
                    $"{identifier}'s state properties can't change until the server restarts.",
                    nameof(properties));
        }
    }

    /// <summary>
    /// Gives a block its properties and its plugin's behaviour, unless the plugin started unloading: then its blocks
    /// were released already, and binding one now would keep the plugin loaded.
    /// </summary>
    /// <returns>Whether the block was bound.</returns>
    internal static bool TryBind(
        BlockType type,
        BlockProperties properties,
        IPluginContainer plugin,
        Func<bool> closing)
    {
        lock (registering)
        {
            if (closing())
                return false;

            var settings = BlockSettings.From(properties, type);
            if (configuredBlocks.Contains(type) && !SameLightAndCollision(type, settings))
            {
                throw new InvalidOperationException(
                    $"{type.Identifier}'s light emission, opacity and collision require a server restart to change.");
            }

            type.Settings = settings;
            configuredBlocks.Add(type);
            type.Binding = new BlockBinding(
                plugin,
                properties.Behavior,
                properties.TreeGrower,
                properties.RandomlyTicks,
                properties.SoundType);
            return true;
        }
    }

    /// <summary>
    /// Whether <paramref name="settings"/> keep <paramref name="type"/>'s collision, opacity and every state's light
    /// level, which placed blocks' light and sky heights were worked out from.
    /// </summary>
    /// <remarks>
    /// Each state's own shapes (<see cref="BlockProperties.StateShapes"/>) may change: they feed neither light nor
    /// heightmaps, and collision, navigation and raycasts read the block's current settings each time, keeping
    /// nothing (a path search's ground cache lasts one search). Sturdy faces follow at once too, and blocks already
    /// resting on a face that stops being sturdy stay until a neighbour update checks them.
    /// </remarks>
    private static bool SameLightAndCollision(BlockType type, BlockSettings settings)
    {
        var current = type.Settings;
        if (current.Opaque != settings.Opaque || current.HasCollision != settings.HasCollision)
            return false;

        for (var index = 0; index < type.StateCount; index++)
        {
            if (current.LightEmissionOf(index) != settings.LightEmissionOf(index))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Drops the behaviour of every block <paramref name="plugin"/> registered, as the plugin unloads. The blocks stay
    /// registered, with their ids and properties, for the world they're in and for the plugin to register again.
    /// </summary>
    /// <returns>The blocks released.</returns>
    internal static List<BlockType> ReleasePlugin(IPluginContainer plugin)
    {
        var released = new List<BlockType>();
        lock (registering)
        {
            foreach (var type in plugins.Types)
            {
                if (type.Binding?.Owner != plugin)
                    continue;

                type.Binding = null;
                released.Add(type);
            }
        }

        return released;
    }

    /// <summary>
    /// The vanilla state whose shapes and light a plugin block's states use: stone's for opaque blocks, glass's for
    /// see-through ones, and cobweb's (no collision, but a full outline to aim at) for blocks without collision.
    /// </summary>
    internal static int TemplateStateOf(BlockType type) => type.Settings switch
    {
        { Opaque: true } => Stone.GetHashCode(),
        { HasCollision: true } => Glass.GetHashCode(),
        _ => Cobweb.GetHashCode()
    };

    private sealed class PluginBlockTable(BlockType[] types, int stateCount)
    {
        public BlockType[] Types { get; } = types;

        public int StateCount { get; } = stateCount;

        public int GlobalBits { get; } = (int)Math.Ceiling(Math.Log2(stateCount));

        public FrozenDictionary<string, BlockType> ByName { get; } =
            types.ToFrozenDictionary(type => type.Identifier, StringComparer.OrdinalIgnoreCase);
    }
}
