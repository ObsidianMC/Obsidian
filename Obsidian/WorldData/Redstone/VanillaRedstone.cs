using Obsidian.API.Blocks;
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

    private static readonly RedstoneComponent wire = new RedstoneWire();

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
    /// Whether entities inside the block press it: pressure plates, and wooden buttons, which arrows press.
    /// </summary>
    public static bool IsPressedByEntities(IBlock state) =>
        For(state) is PressurePlate or Switch { PressedByArrows: true } or DetectorRailComponent;

    private static RedstoneComponent?[] Build()
    {
        var table = new RedstoneComponent?[BlocksRegistry.Names.Length];
        var torch = new RedstoneTorch(wall: false);
        var wallTorch = new RedstoneTorch(wall: true);
        var repeater = new Diode(DiodeKind.Repeater);
        var comparator = new Diode(DiodeKind.Comparator);
        var lamp = new RedstoneLamp();
        var redstoneBlock = new RedstoneBlock();
        var hopper = new HopperComponent();
        var dispenser = new DispenserComponent(dropper: false);
        var dropper = new DispenserComponent(dropper: true);
        var tnt = new TntComponent();
        var lectern = new LecternComponent();
        var crafter = new CrafterComponent();
        var rail = new RailComponent();
        var poweredRail = new PoweredRailComponent();
        var detectorRail = new DetectorRailComponent();

        for (var id = 0; id < table.Length; id++)
        {
            var block = BlocksRegistry.GetDefault(id);
            var name = block.UnlocalizedName;

            table[id] = block.BlockClass() switch
            {
                "RedStoneWireBlock" => wire,
                "RedstoneTorchBlock" => torch,
                "RedstoneWallTorchBlock" => wallTorch,
                "RepeaterBlock" => repeater,
                "ComparatorBlock" => comparator,
                "LeverBlock" => Switch.Lever,
                "ButtonBlock" => Switch.Button(name),
                "PressurePlateBlock" => PressurePlate.For(name),
                "WeightedPressurePlateBlock" => PressurePlate.Weighted(name),
                "RedstoneLampBlock" => lamp,
                "PoweredBlock" => redstoneBlock,
                "HopperBlock" => hopper,
                "DispenserBlock" => dispenser,
                "DropperBlock" => dropper,
                "TntBlock" => tnt,
                "LecternBlock" => lectern,
                "CrafterBlock" => crafter,
                "RailBlock" => rail,
                "PoweredRailBlock" => poweredRail,
                "DetectorRailBlock" => detectorRail,
                _ => null
            };
        }

        return table;
    }
}
