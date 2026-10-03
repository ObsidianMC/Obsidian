using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// The mossy cobblestone jungle temple with its tripwire and lever traps, like vanilla's <c>JungleTempleStructure</c>.
/// </summary>
[StructureType("minecraft:jungle_temple")]
public sealed class JungleTempleStructure() : SinglePieceStructure(12, 15)
{
    protected override StructurePiece CreatePiece(IRandomSource random, int x, int z) => new JungleTemplePiece(random, x, z);
}

/// <summary>
/// Vanilla's <c>JungleTemplePiece</c>.
/// </summary>
public sealed class JungleTemplePiece : ScatteredFeaturePiece
{
    private const string DispenserLoot = "minecraft:chests/jungle_temple_dispenser";
    private const string ChestLoot = "minecraft:chests/jungle_temple";

    private static readonly IBlock air = BlocksRegistry.Air;
    private static readonly IBlock mossyCobblestone = BlocksRegistry.Get(Material.MossyCobblestone);
    private static readonly IBlock chiseledStoneBricks = BlocksRegistry.Get(Material.ChiseledStoneBricks);
    private static readonly IBlock cobblestoneStairs = BlocksRegistry.Get(Material.CobblestoneStairs);
    private static readonly IBlock tripwireHook = BlocksRegistry.Get(Material.TripwireHook);
    private static readonly IBlock tripwire = BlocksRegistry.Get(Material.Tripwire);
    private static readonly IBlock redstoneWire = BlocksRegistry.Get(Material.RedstoneWire);
    private static readonly IBlock vine = BlocksRegistry.Get(Material.Vine);

    private bool placedMainChest;
    private bool placedHiddenChest;
    private bool placedTrap1;
    private bool placedTrap2;

    public JungleTemplePiece(IRandomSource random, int x, int z) : base(x, 64, z, 12, 10, 15, RandomHorizontalDirection(random))
    {
    }

    internal override void SaveState(NbtCompound tag)
    {
        base.SaveState(tag);

        tag.Add(new NbtTag<bool>("placedMainChest", this.placedMainChest));
        tag.Add(new NbtTag<bool>("placedHiddenChest", this.placedHiddenChest));
        tag.Add(new NbtTag<bool>("placedTrap1", this.placedTrap1));
        tag.Add(new NbtTag<bool>("placedTrap2", this.placedTrap2));
    }

    internal override void LoadState(NbtCompound tag)
    {
        base.LoadState(tag);

        this.placedMainChest = tag.TryGetBool("placedMainChest", out var mainChest) && mainChest;
        this.placedHiddenChest = tag.TryGetBool("placedHiddenChest", out var hiddenChest) && hiddenChest;
        this.placedTrap1 = tag.TryGetBool("placedTrap1", out var trap1) && trap1;
        this.placedTrap2 = tag.TryGetBool("placedTrap2", out var trap2) && trap2;
    }

    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var box = context.Box;
        var random = context.Random;
        if (!this.UpdateAverageGroundHeight(level, box, 0))
            return;

        var stone = new MossStoneSelector();
        void Stone(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) =>
            this.GenerateBox(level, box, minX, minY, minZ, maxX, maxY, maxZ, false, random, stone);
        void Air(int minX, int minY, int minZ, int maxX, int maxY, int maxZ) =>
            this.GenerateAirBox(level, box, minX, minY, minZ, maxX, maxY, maxZ);
        void Place(IBlock block, int x, int y, int z) => this.PlaceBlock(level, block, x, y, z, box);

        Stone(0, -4, 0, this.Width - 1, 0, this.Depth - 1);
        Stone(2, 1, 2, 9, 2, 2);
        Stone(2, 1, 12, 9, 2, 12);
        Stone(2, 1, 3, 2, 2, 11);
        Stone(9, 1, 3, 9, 2, 11);
        Stone(1, 3, 1, 10, 6, 1);
        Stone(1, 3, 13, 10, 6, 13);
        Stone(1, 3, 2, 1, 6, 12);
        Stone(10, 3, 2, 10, 6, 12);
        Stone(2, 3, 2, 9, 3, 12);
        Stone(2, 6, 2, 9, 6, 12);
        Stone(3, 7, 3, 8, 7, 11);
        Stone(4, 8, 4, 7, 8, 10);
        Air(3, 1, 3, 8, 2, 11);
        Air(4, 3, 6, 7, 3, 9);
        Air(2, 4, 2, 9, 5, 12);
        Air(4, 6, 5, 7, 6, 9);
        Air(5, 7, 6, 6, 7, 8);
        Air(5, 1, 2, 6, 2, 2);
        Air(5, 2, 12, 6, 2, 12);
        Air(5, 5, 1, 6, 5, 1);
        Air(5, 5, 13, 6, 5, 13);
        Place(air, 1, 5, 5);
        Place(air, 10, 5, 5);
        Place(air, 1, 5, 9);
        Place(air, 10, 5, 9);

        for (var z = 0; z <= 14; z += 14)
        {
            Stone(2, 4, z, 2, 5, z);
            Stone(4, 4, z, 4, 5, z);
            Stone(7, 4, z, 7, 5, z);
            Stone(9, 4, z, 9, 5, z);
        }

        Stone(5, 6, 0, 6, 6, 0);

        for (var x = 0; x <= 11; x += 11)
        {
            for (var z = 2; z <= 12; z += 2)
                Stone(x, 4, z, x, 5, z);

            Stone(x, 6, 5, x, 6, 5);
            Stone(x, 6, 9, x, 6, 9);
        }

        Stone(2, 7, 2, 2, 9, 2);
        Stone(9, 7, 2, 9, 9, 2);
        Stone(2, 7, 12, 2, 9, 12);
        Stone(9, 7, 12, 9, 9, 12);
        Stone(4, 9, 4, 4, 9, 4);
        Stone(7, 9, 4, 7, 9, 4);
        Stone(4, 9, 10, 4, 9, 10);
        Stone(7, 9, 10, 7, 9, 10);
        Stone(5, 9, 7, 6, 9, 7);

        var east = cobblestoneStairs.WithProperty("facing", "east");
        var west = cobblestoneStairs.WithProperty("facing", "west");
        var south = cobblestoneStairs.WithProperty("facing", "south");
        var north = cobblestoneStairs.WithProperty("facing", "north");
        Place(north, 5, 9, 6);
        Place(north, 6, 9, 6);
        Place(south, 5, 9, 8);
        Place(south, 6, 9, 8);
        Place(north, 4, 0, 0);
        Place(north, 5, 0, 0);
        Place(north, 6, 0, 0);
        Place(north, 7, 0, 0);
        Place(north, 4, 1, 8);
        Place(north, 4, 2, 9);
        Place(north, 4, 3, 10);
        Place(north, 7, 1, 8);
        Place(north, 7, 2, 9);
        Place(north, 7, 3, 10);
        Stone(4, 1, 9, 4, 1, 9);
        Stone(7, 1, 9, 7, 1, 9);
        Stone(4, 1, 10, 7, 2, 10);
        Stone(5, 4, 5, 6, 4, 5);
        Place(east, 4, 4, 5);
        Place(west, 7, 4, 5);

        for (var i = 0; i < 4; i++)
        {
            Place(south, 5, -i, 6 + i);
            Place(south, 6, -i, 6 + i);
            Air(5, -i, 7 + i, 6, -i, 9 + i);
        }

        Air(1, -3, 12, 10, -1, 13);
        Air(1, -3, 1, 3, -1, 13);
        Air(1, -3, 1, 9, -1, 5);

        for (var z = 1; z <= 13; z += 2)
            Stone(1, -3, z, 1, -2, z);

        for (var z = 2; z <= 12; z += 2)
            Stone(1, -1, z, 3, -1, z);

        Stone(2, -2, 1, 5, -2, 1);
        Stone(7, -2, 1, 9, -2, 1);
        Stone(6, -3, 1, 6, -3, 1);
        Stone(6, -1, 1, 6, -1, 1);

        // The arrow trap across the stairs.
        Place(Hook("east"), 1, -3, 8);
        Place(Hook("west"), 4, -3, 8);
        Place(Tripwire("east", "west"), 2, -3, 8);
        Place(Tripwire("east", "west"), 3, -3, 8);

        var wireNorthSouth = Wire(("north", "side"), ("south", "side"));
        Place(wireNorthSouth, 5, -3, 7);
        Place(wireNorthSouth, 5, -3, 6);
        Place(wireNorthSouth, 5, -3, 5);
        Place(wireNorthSouth, 5, -3, 4);
        Place(wireNorthSouth, 5, -3, 3);
        Place(wireNorthSouth, 5, -3, 2);
        Place(Wire(("north", "side"), ("west", "side")), 5, -3, 1);
        Place(Wire(("east", "side"), ("west", "side")), 4, -3, 1);
        Place(mossyCobblestone, 3, -3, 1);
        if (!this.placedTrap1)
            this.placedTrap1 = this.CreateDispenser(level, box, random, 3, -2, 1, BlockFace.North, DispenserLoot);

        Place(vine.WithProperty("south", true), 3, -2, 2);

        // The arrow trap in the side corridor.
        Place(Hook("north"), 7, -3, 1);
        Place(Hook("south"), 7, -3, 5);
        Place(Tripwire("north", "south"), 7, -3, 2);
        Place(Tripwire("north", "south"), 7, -3, 3);
        Place(Tripwire("north", "south"), 7, -3, 4);
        Place(Wire(("east", "side"), ("west", "side")), 8, -3, 6);
        Place(Wire(("west", "side"), ("south", "side")), 9, -3, 6);
        Place(Wire(("north", "side"), ("south", "up")), 9, -3, 5);
        Place(mossyCobblestone, 9, -3, 4);
        Place(wireNorthSouth, 9, -2, 4);
        if (!this.placedTrap2)
            this.placedTrap2 = this.CreateDispenser(level, box, random, 9, -2, 3, BlockFace.West, DispenserLoot);

        Place(vine.WithProperty("east", true), 8, -1, 3);
        Place(vine.WithProperty("east", true), 8, -2, 3);
        if (!this.placedMainChest)
            this.placedMainChest = this.CreateChest(level, box, random, 8, -3, 3, ChestLoot);

        Place(mossyCobblestone, 9, -3, 2);
        Place(mossyCobblestone, 8, -3, 1);
        Place(mossyCobblestone, 4, -3, 5);
        Place(mossyCobblestone, 5, -2, 5);
        Place(mossyCobblestone, 5, -1, 5);
        Place(mossyCobblestone, 6, -3, 5);
        Place(mossyCobblestone, 7, -2, 5);
        Place(mossyCobblestone, 7, -1, 5);
        Place(mossyCobblestone, 8, -3, 5);
        Stone(9, -1, 1, 9, -1, 5);

        // The lever puzzle and the hidden chest.
        Air(8, -3, 8, 10, -1, 10);
        Place(chiseledStoneBricks, 8, -2, 11);
        Place(chiseledStoneBricks, 9, -2, 11);
        Place(chiseledStoneBricks, 10, -2, 11);
        var lever = BlocksRegistry.Get(Material.Lever).WithProperty("facing", "north").WithProperty("face", "wall");
        Place(lever, 8, -2, 12);
        Place(lever, 9, -2, 12);
        Place(lever, 10, -2, 12);
        Stone(8, -3, 8, 8, -3, 10);
        Stone(10, -3, 8, 10, -3, 10);
        Place(mossyCobblestone, 10, -2, 9);
        Place(wireNorthSouth, 8, -2, 9);
        Place(wireNorthSouth, 8, -2, 10);
        Place(Wire(("north", "side"), ("south", "side"), ("east", "side"), ("west", "side")), 10, -1, 9);
        var stickyPiston = BlocksRegistry.Get(Material.StickyPiston);
        Place(stickyPiston.WithProperty("facing", "up"), 9, -2, 8);
        Place(stickyPiston.WithProperty("facing", "west"), 10, -2, 8);
        Place(stickyPiston.WithProperty("facing", "west"), 10, -1, 8);
        Place(BlocksRegistry.Get(Material.Repeater).WithProperty("facing", "north"), 10, -2, 10);
        if (!this.placedHiddenChest)
            this.placedHiddenChest = this.CreateChest(level, box, random, 9, -3, 10, ChestLoot);
    }

    private static IBlock Hook(string facing) => tripwireHook.WithProperty("facing", facing).WithProperty("attached", true);

    private static IBlock Tripwire(string first, string second) =>
        tripwire.WithProperty(first, true).WithProperty(second, true).WithProperty("attached", true);

    private static IBlock Wire(params (string Side, string Connection)[] sides)
    {
        var wire = redstoneWire;
        foreach (var (side, connection) in sides)
            wire = wire.WithProperty(side, connection);

        return wire;
    }

    /// <summary>
    /// Vanilla's <c>JungleTemplePiece.MossStoneSelector</c>: cobblestone 40% of the time, mossy cobblestone otherwise.
    /// </summary>
    private sealed class MossStoneSelector : BlockSelector
    {
        private static readonly IBlock cobblestone = BlocksRegistry.Get(Material.Cobblestone);

        public override void Next(IRandomSource random, int x, int y, int z, bool isEdge) =>
            this.NextBlock = random.NextFloat() < 0.4f ? cobblestone : mossyCobblestone;
    }
}
