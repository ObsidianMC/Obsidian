using Obsidian.API.Inventory;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Vanilla's redstone components by block: dust, torches, repeaters, comparators, levers, buttons, pressure plates,
/// the redstone block, the redstone lamp, rails, and the power switches of hoppers, droppers and dispensers.
/// </summary>
internal static class VanillaRedstone
{
    private static readonly Lazy<RedstoneComponent?[]> components = new(Build);

    /// <summary>
    /// The component of a vanilla block, or null for other blocks (plugins' blocks included).
    /// </summary>
    public static RedstoneComponent? For(IBlock state)
    {
        var table = components.Value;
        var id = state.RegistryId;

        return (uint)id < (uint)table.Length ? table[id] : null;
    }

    /// <summary>
    /// The block an item places that isn't named like it: redstone dust from <c>minecraft:redstone</c>.
    /// </summary>
    public static bool TryGetPlacedBlock(Item item, [NotNullWhen(true)] out IBlock? block)
    {
        block = item.Id == ItemIds.Redstone ? BlocksRegistry.RedstoneWire : null;
        return block is not null;
    }

    /// <summary>
    /// Whether entities inside the block press it: the detector rail.
    /// </summary>
    public static bool IsPressedByEntities(IBlock state) => For(state) is DetectorRailComponent;

    private static RedstoneComponent?[] Build()
    {
        var table = new RedstoneComponent?[BlocksRegistry.Names.Length];
        var rail = new RailComponent();
        var poweredRail = new PoweredRailComponent();
        var detectorRail = new DetectorRailComponent();

        for (var id = 0; id < table.Length; id++)
        {
            var block = BlocksRegistry.GetDefault(id);
            // Everything else is the engine's (see RedstoneRegistration). What's left here are the three rails, which
            // still need the synchronous LevelBlockUpdates path: entity-inside checks, signal-source checks, and the
            // unsupported-rail drop in BlockUpdates.NeighborChanged, none of which the block-notification pipeline has yet.
            table[id] = block.BlockClass() switch
            {
                "RailBlock" => rail,
                "PoweredRailBlock" => poweredRail,
                "DetectorRailBlock" => detectorRail,
                _ => null
            };
        }

        return table;
    }
}
