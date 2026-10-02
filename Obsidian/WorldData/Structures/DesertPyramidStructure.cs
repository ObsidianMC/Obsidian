using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;
using System.Threading;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// The sandstone desert pyramid with its TNT trap room and a buried cellar of suspicious sand, like vanilla's
/// <c>DesertPyramidStructure</c>.
/// </summary>
[StructureType("minecraft:desert_pyramid")]
public sealed class DesertPyramidStructure() : SinglePieceStructure(21, 21)
{
    private const string ArchaeologyLoot = "minecraft:archaeology/desert_pyramid";

    protected override StructurePiece CreatePiece(IRandomSource random, int x, int z) => new DesertPyramidPiece(random, x, z);

    /// <summary>
    /// Vanilla <c>afterPlace</c>: 5-7 of the cellar's sand spots (picked the same way in every chunk) and the collapsed
    /// roof spot become suspicious sand; the other spots become sand.
    /// </summary>
    internal override void AfterPlace(StructurePieceContext context, IReadOnlyList<StructurePiece> pieces)
    {
        var level = context.Level;
        var box = context.Box;

        // Vanilla collects the spots in a set sorted by Vec3i.compareTo: Y, then Z, then X.
        var spots = new SortedSet<Vector>(Comparer<Vector>.Create((a, b) =>
            a.Y != b.Y ? a.Y.CompareTo(b.Y) : a.Z != b.Z ? a.Z.CompareTo(b.Z) : a.X.CompareTo(b.X)));

        foreach (var piece in pieces.OfType<DesertPyramidPiece>())
        {
            spots.UnionWith(piece.GetPotentialSuspiciousSandWorldPositions());
            PlaceSuspiciousSand(level, box, piece.RandomCollapsedRoofPosition);
        }

        var shuffled = spots.ToList();
        var center = BoxCenter(pieces);
        var random = new LegacyRandomSource(level.Seed).ForkPositional().At(center.X, center.Y, center.Z);
        FeatureHelpers.Shuffle(shuffled, random);

        var suspicious = Math.Min(spots.Count, 5 + random.NextInt(3));
        foreach (var position in shuffled)
        {
            if (suspicious > 0)
            {
                suspicious--;
                PlaceSuspiciousSand(level, box, position);
            }
            else if (box.IsInside(position))
            {
                level.SetBlock(position, BlocksRegistry.Get(Material.Sand));
            }
        }
    }

    private static void PlaceSuspiciousSand(IWorldGenLevel level, BlockBox box, Vector position)
    {
        if (!box.IsInside(position))
            return;

        level.SetBlock(position, BlocksRegistry.Get(Material.SuspiciousSand));
        FeatureHelpers.SetBrushableLootTable(level, position, ArchaeologyLoot, FeatureHelpers.AsLong(position));
    }

    private static Vector BoxCenter(IReadOnlyList<StructurePiece> pieces) =>
        BlockBox.Encapsulating(pieces.Select(piece => piece.BoundingBox))!.Value.Center;
}

/// <summary>
/// Vanilla's <c>DesertPyramidPiece</c>.
/// </summary>
public sealed class DesertPyramidPiece : ScatteredFeaturePiece
{
    private const string ChestLoot = "minecraft:chests/desert_pyramid";

    private static readonly IBlock air = BlocksRegistry.Air;
    private static readonly IBlock sandstone = BlocksRegistry.Get(Material.Sandstone);
    private static readonly IBlock cutSandstone = BlocksRegistry.Get(Material.CutSandstone);
    private static readonly IBlock chiseledSandstone = BlocksRegistry.Get(Material.ChiseledSandstone);
    private static readonly IBlock sandstoneStairs = BlocksRegistry.Get(Material.SandstoneStairs);
    private static readonly IBlock orangeTerracotta = BlocksRegistry.Get(Material.OrangeTerracotta);
    private static readonly IBlock blueTerracotta = BlocksRegistry.Get(Material.BlueTerracotta);
    private static readonly IBlock sand = BlocksRegistry.Get(Material.Sand);

    private readonly bool[] hasPlacedChest = new bool[4];

    // Every placement adds the cellar's sand spots again; afterPlace removes the duplicates.
    private readonly List<Vector> potentialSuspiciousSandWorldPositions = [];
    private readonly Lock sandLock = new();

    public DesertPyramidPiece(IRandomSource random, int x, int z) : base(x, 64, z, 21, 15, 21, RandomHorizontalDirection(random))
    {
    }

    public Vector RandomCollapsedRoofPosition { get; private set; } = Vector.Zero;

    public IReadOnlyList<Vector> GetPotentialSuspiciousSandWorldPositions()
    {
        lock (this.sandLock)
            return [.. this.potentialSuspiciousSandWorldPositions];
    }

    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var box = context.Box;
        var random = context.Random;

        // Vanilla draws the offset in every chunk, even once the height is known.
        if (!this.UpdateHeightPositionToLowestGroundHeight(level, -random.NextInt(3)))
            return;

        void Box(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, IBlock edge, IBlock inside) =>
            this.GenerateBox(level, box, minX, minY, minZ, maxX, maxY, maxZ, edge, inside, false);
        void Place(IBlock block, int x, int y, int z) => this.PlaceBlock(level, block, x, y, z, box);

        var width = this.Width;
        var depth = this.Depth;
        Box(0, -4, 0, width - 1, 0, depth - 1, sandstone, sandstone);
        for (var layer = 1; layer <= 9; layer++)
        {
            Box(layer, layer, layer, width - 1 - layer, layer, depth - 1 - layer, sandstone, sandstone);
            Box(layer + 1, layer, layer + 1, width - 2 - layer, layer, depth - 2 - layer, air, air);
        }

        for (var x = 0; x < width; x++)
        {
            for (var z = 0; z < depth; z++)
                this.FillColumnDown(level, sandstone, x, -5, z, box);
        }

        var north = sandstoneStairs.WithProperty("facing", "north");
        var south = sandstoneStairs.WithProperty("facing", "south");
        var east = sandstoneStairs.WithProperty("facing", "east");
        var west = sandstoneStairs.WithProperty("facing", "west");

        // Towers.
        Box(0, 0, 0, 4, 9, 4, sandstone, air);
        Box(1, 10, 1, 3, 10, 3, sandstone, sandstone);
        Place(north, 2, 10, 0);
        Place(south, 2, 10, 4);
        Place(east, 0, 10, 2);
        Place(west, 4, 10, 2);
        Box(width - 5, 0, 0, width - 1, 9, 4, sandstone, air);
        Box(width - 4, 10, 1, width - 2, 10, 3, sandstone, sandstone);
        Place(north, width - 3, 10, 0);
        Place(south, width - 3, 10, 4);
        Place(east, width - 5, 10, 2);
        Place(west, width - 1, 10, 2);

        // Entrance and halls.
        Box(8, 0, 0, 12, 4, 4, sandstone, air);
        Box(9, 1, 0, 11, 3, 4, air, air);
        Place(cutSandstone, 9, 1, 1);
        Place(cutSandstone, 9, 2, 1);
        Place(cutSandstone, 9, 3, 1);
        Place(cutSandstone, 10, 3, 1);
        Place(cutSandstone, 11, 3, 1);
        Place(cutSandstone, 11, 2, 1);
        Place(cutSandstone, 11, 1, 1);
        Box(4, 1, 1, 8, 3, 3, sandstone, air);
        Box(4, 1, 2, 8, 2, 2, air, air);
        Box(12, 1, 1, 16, 3, 3, sandstone, air);
        Box(12, 1, 2, 16, 2, 2, air, air);
        Box(5, 4, 5, width - 6, 4, depth - 6, sandstone, sandstone);
        Box(9, 4, 9, 11, 4, 11, air, air);
        Box(8, 1, 8, 8, 3, 8, cutSandstone, cutSandstone);
        Box(12, 1, 8, 12, 3, 8, cutSandstone, cutSandstone);
        Box(8, 1, 12, 8, 3, 12, cutSandstone, cutSandstone);
        Box(12, 1, 12, 12, 3, 12, cutSandstone, cutSandstone);
        Box(1, 1, 5, 4, 4, 11, sandstone, sandstone);
        Box(width - 5, 1, 5, width - 2, 4, 11, sandstone, sandstone);
        Box(6, 7, 9, 6, 7, 11, sandstone, sandstone);
        Box(width - 7, 7, 9, width - 7, 7, 11, sandstone, sandstone);
        Box(5, 5, 9, 5, 7, 11, cutSandstone, cutSandstone);
        Box(width - 6, 5, 9, width - 6, 7, 11, cutSandstone, cutSandstone);
        Place(air, 5, 5, 10);
        Place(air, 5, 6, 10);
        Place(air, 6, 6, 10);
        Place(air, width - 6, 5, 10);
        Place(air, width - 6, 6, 10);
        Place(air, width - 7, 6, 10);
        Box(2, 4, 4, 2, 6, 4, air, air);
        Box(width - 3, 4, 4, width - 3, 6, 4, air, air);
        Place(north, 2, 4, 5);
        Place(north, 2, 3, 4);
        Place(north, width - 3, 4, 5);
        Place(north, width - 3, 3, 4);
        Box(1, 1, 3, 2, 2, 3, sandstone, sandstone);
        Box(width - 3, 1, 3, width - 2, 2, 3, sandstone, sandstone);
        Place(sandstone, 1, 1, 2);
        Place(sandstone, width - 2, 1, 2);
        Place(BlocksRegistry.Get(Material.SandstoneSlab), 1, 2, 2);
        Place(BlocksRegistry.Get(Material.SandstoneSlab), width - 2, 2, 2);
        Place(west, 2, 1, 2);
        Place(east, width - 3, 1, 2);
        Box(4, 3, 5, 4, 3, 17, sandstone, sandstone);
        Box(width - 5, 3, 5, width - 5, 3, 17, sandstone, sandstone);
        Box(3, 1, 5, 4, 2, 16, air, air);
        Box(width - 6, 1, 5, width - 5, 2, 16, air, air);

        for (var z = 5; z <= 17; z += 2)
        {
            Place(cutSandstone, 4, 1, z);
            Place(chiseledSandstone, 4, 2, z);
            Place(cutSandstone, width - 5, 1, z);
            Place(chiseledSandstone, width - 5, 2, z);
        }

        // The floor pattern over the trap room.
        Place(orangeTerracotta, 10, 0, 7);
        Place(orangeTerracotta, 10, 0, 8);
        Place(orangeTerracotta, 9, 0, 9);
        Place(orangeTerracotta, 11, 0, 9);
        Place(orangeTerracotta, 8, 0, 10);
        Place(orangeTerracotta, 12, 0, 10);
        Place(orangeTerracotta, 7, 0, 10);
        Place(orangeTerracotta, 13, 0, 10);
        Place(orangeTerracotta, 9, 0, 11);
        Place(orangeTerracotta, 11, 0, 11);
        Place(orangeTerracotta, 10, 0, 12);
        Place(orangeTerracotta, 10, 0, 13);
        Place(blueTerracotta, 10, 0, 10);

        // Tower facades.
        for (var x = 0; x <= width - 1; x += width - 1)
        {
            Place(cutSandstone, x, 2, 1);
            Place(orangeTerracotta, x, 2, 2);
            Place(cutSandstone, x, 2, 3);
            Place(cutSandstone, x, 3, 1);
            Place(orangeTerracotta, x, 3, 2);
            Place(cutSandstone, x, 3, 3);
            Place(orangeTerracotta, x, 4, 1);
            Place(chiseledSandstone, x, 4, 2);
            Place(orangeTerracotta, x, 4, 3);
            Place(cutSandstone, x, 5, 1);
            Place(orangeTerracotta, x, 5, 2);
            Place(cutSandstone, x, 5, 3);
            Place(orangeTerracotta, x, 6, 1);
            Place(chiseledSandstone, x, 6, 2);
            Place(orangeTerracotta, x, 6, 3);
            Place(orangeTerracotta, x, 7, 1);
            Place(orangeTerracotta, x, 7, 2);
            Place(orangeTerracotta, x, 7, 3);
            Place(cutSandstone, x, 8, 1);
            Place(cutSandstone, x, 8, 2);
            Place(cutSandstone, x, 8, 3);
        }

        for (var x = 2; x <= width - 3; x += width - 3 - 2)
        {
            Place(cutSandstone, x - 1, 2, 0);
            Place(orangeTerracotta, x, 2, 0);
            Place(cutSandstone, x + 1, 2, 0);
            Place(cutSandstone, x - 1, 3, 0);
            Place(orangeTerracotta, x, 3, 0);
            Place(cutSandstone, x + 1, 3, 0);
            Place(orangeTerracotta, x - 1, 4, 0);
            Place(chiseledSandstone, x, 4, 0);
            Place(orangeTerracotta, x + 1, 4, 0);
            Place(cutSandstone, x - 1, 5, 0);
            Place(orangeTerracotta, x, 5, 0);
            Place(cutSandstone, x + 1, 5, 0);
            Place(orangeTerracotta, x - 1, 6, 0);
            Place(chiseledSandstone, x, 6, 0);
            Place(orangeTerracotta, x + 1, 6, 0);
            Place(orangeTerracotta, x - 1, 7, 0);
            Place(orangeTerracotta, x, 7, 0);
            Place(orangeTerracotta, x + 1, 7, 0);
            Place(cutSandstone, x - 1, 8, 0);
            Place(cutSandstone, x, 8, 0);
            Place(cutSandstone, x + 1, 8, 0);
        }

        Box(8, 4, 0, 12, 6, 0, cutSandstone, cutSandstone);
        Place(air, 8, 6, 0);
        Place(air, 12, 6, 0);
        Place(orangeTerracotta, 9, 5, 0);
        Place(chiseledSandstone, 10, 5, 0);
        Place(orangeTerracotta, 11, 5, 0);

        // The trap room.
        Box(8, -14, 8, 12, -11, 12, cutSandstone, cutSandstone);
        Box(8, -10, 8, 12, -10, 12, chiseledSandstone, chiseledSandstone);
        Box(8, -9, 8, 12, -9, 12, cutSandstone, cutSandstone);
        Box(8, -8, 8, 12, -1, 12, sandstone, sandstone);
        Box(9, -11, 9, 11, -1, 11, air, air);
        Place(BlocksRegistry.Get(Material.StonePressurePlate), 10, -11, 10);
        Box(9, -13, 9, 11, -13, 11, BlocksRegistry.Get(Material.Tnt), air);
        Place(air, 8, -11, 10);
        Place(air, 8, -10, 10);
        Place(chiseledSandstone, 7, -10, 10);
        Place(cutSandstone, 7, -11, 10);
        Place(air, 12, -11, 10);
        Place(air, 12, -10, 10);
        Place(chiseledSandstone, 13, -10, 10);
        Place(cutSandstone, 13, -11, 10);
        Place(air, 10, -11, 8);
        Place(air, 10, -10, 8);
        Place(chiseledSandstone, 10, -10, 7);
        Place(cutSandstone, 10, -11, 7);
        Place(air, 10, -11, 12);
        Place(air, 10, -10, 12);
        Place(chiseledSandstone, 10, -10, 13);
        Place(cutSandstone, 10, -11, 13);

        foreach (var face in FeatureHelpers.Horizontal)
        {
            var index = Data2D(face);
            if (this.hasPlacedChest[index])
                continue;

            var step = face.ToVector() * 2;
            this.hasPlacedChest[index] = this.CreateChest(level, box, random, 10 + step.X, -11, 10 + step.Z, ChestLoot);
        }

        this.AddCellar(level, box);
    }

    private void AddCellar(IWorldGenLevel level, BlockBox box)
    {
        var center = new Vector(16, -4, 13);
        this.AddCellarStairs(center, level, box);
        this.AddCellarRoom(center, level, box);
    }

    private void AddCellarStairs(Vector center, IWorldGenLevel level, BlockBox box)
    {
        var (x, y, z) = (center.X, center.Y, center.Z);
        var stairs = sandstoneStairs.Rotate(StructureRotation.CounterClockwise90);
        this.PlaceBlock(level, stairs, 13, -1, 17, box);
        this.PlaceBlock(level, stairs, 14, -2, 17, box);
        this.PlaceBlock(level, stairs, 15, -3, 17, box);

        // Like vanilla, this choice comes from the level's random, not the structure's.
        var sandFirst = level.Random.NextBoolean();
        this.PlaceBlock(level, sand, x - 4, y + 4, z + 4, box);
        this.PlaceBlock(level, sand, x - 3, y + 4, z + 4, box);
        this.PlaceBlock(level, sand, x - 2, y + 4, z + 4, box);
        this.PlaceBlock(level, sand, x - 1, y + 4, z + 4, box);
        this.PlaceBlock(level, sand, x, y + 4, z + 4, box);
        this.PlaceBlock(level, sand, x - 2, y + 3, z + 4, box);
        this.PlaceBlock(level, sandFirst ? sand : sandstone, x - 1, y + 3, z + 4, box);
        this.PlaceBlock(level, !sandFirst ? sand : sandstone, x, y + 3, z + 4, box);
        this.PlaceBlock(level, sand, x - 1, y + 2, z + 4, box);
        this.PlaceBlock(level, sandstone, x, y + 2, z + 4, box);
        this.PlaceBlock(level, sand, x, y + 1, z + 4, box);
    }

    private void AddCellarRoom(Vector center, IWorldGenLevel level, BlockBox box)
    {
        var (x, y, z) = (center.X, center.Y, center.Z);
        void Wall(int minX, int minY, int minZ, int maxX, int maxY, int maxZ, IBlock block) =>
            this.GenerateBox(level, box, minX, minY, minZ, maxX, maxY, maxZ, block, block, true);
        void Place(IBlock block, int px, int py, int pz) => this.PlaceBlock(level, block, px, py, pz, box);

        Wall(x - 3, y + 1, z - 3, x - 3, y + 1, z + 2, cutSandstone);
        Wall(x + 3, y + 1, z - 3, x + 3, y + 1, z + 2, cutSandstone);
        Wall(x - 3, y + 1, z - 3, x + 3, y + 1, z - 2, cutSandstone);
        Wall(x - 3, y + 1, z + 3, x + 3, y + 1, z + 3, cutSandstone);
        Wall(x - 3, y + 2, z - 3, x - 3, y + 2, z + 2, chiseledSandstone);
        Wall(x + 3, y + 2, z - 3, x + 3, y + 2, z + 2, chiseledSandstone);
        Wall(x - 3, y + 2, z - 3, x + 3, y + 2, z - 2, chiseledSandstone);
        Wall(x - 3, y + 2, z + 3, x + 3, y + 2, z + 3, chiseledSandstone);
        Wall(x - 3, -1, z - 3, x - 3, -1, z + 2, cutSandstone);
        Wall(x + 3, -1, z - 3, x + 3, -1, z + 2, cutSandstone);
        Wall(x - 3, -1, z - 3, x + 3, -1, z - 2, cutSandstone);
        Wall(x - 3, -1, z + 3, x + 3, -1, z + 3, cutSandstone);
        this.PlaceSandBox(x - 2, y + 1, z - 2, x + 2, y + 3, z + 2);
        this.PlaceCollapsedRoof(level, box, x - 2, y + 4, z - 2, x + 2, z + 2);

        Place(blueTerracotta, x, y, z);
        Place(orangeTerracotta, x + 1, y, z - 1);
        Place(orangeTerracotta, x + 1, y, z + 1);
        Place(orangeTerracotta, x - 1, y, z - 1);
        Place(orangeTerracotta, x - 1, y, z + 1);
        Place(orangeTerracotta, x + 2, y, z);
        Place(orangeTerracotta, x - 2, y, z);
        Place(orangeTerracotta, x, y, z + 2);
        Place(orangeTerracotta, x, y, z - 2);
        Place(orangeTerracotta, x + 3, y, z);
        this.PlaceSand(x + 3, y + 1, z);
        this.PlaceSand(x + 3, y + 2, z);
        Place(cutSandstone, x + 4, y + 1, z);
        Place(chiseledSandstone, x + 4, y + 2, z);
        Place(orangeTerracotta, x - 3, y, z);
        this.PlaceSand(x - 3, y + 1, z);
        this.PlaceSand(x - 3, y + 2, z);
        Place(cutSandstone, x - 4, y + 1, z);
        Place(chiseledSandstone, x - 4, y + 2, z);
        Place(orangeTerracotta, x, y, z + 3);
        this.PlaceSand(x, y + 1, z + 3);
        this.PlaceSand(x, y + 2, z + 3);
        Place(orangeTerracotta, x, y, z - 3);
        this.PlaceSand(x, y + 1, z - 3);
        this.PlaceSand(x, y + 2, z - 3);
        Place(cutSandstone, x, y + 1, z - 4);
        Place(chiseledSandstone, x, -2, z - 4);
    }

    private void PlaceSand(int x, int y, int z)
    {
        lock (this.sandLock)
            this.potentialSuspiciousSandWorldPositions.Add(this.GetWorldPos(x, y, z));
    }

    private void PlaceSandBox(int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                    this.PlaceSand(x, y, z);
            }
        }
    }

    private void PlaceCollapsedRoof(IWorldGenLevel level, BlockBox box, int minX, int y, int minZ, int maxX, int maxZ)
    {
        for (var x = minX; x <= maxX; x++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                // The level's random, like vanilla.
                this.PlaceBlock(level, level.Random.NextFloat() < 0.33f ? sandstone : sand, x, y, z, box);
            }
        }

        var corner = this.GetWorldPos(minX, y, minZ);
        var random = new LegacyRandomSource(level.Seed).ForkPositional().At(corner.X, corner.Y, corner.Z);
        var roofX = random.NextIntBetweenInclusive(minX, maxX);
        var roofZ = random.NextIntBetweenInclusive(minZ, maxZ);
        this.RandomCollapsedRoofPosition = new Vector(this.GetWorldX(roofX, roofZ), this.GetWorldY(y), this.GetWorldZ(roofX, roofZ));
    }

    /// <summary>Vanilla <c>Direction.get2DDataValue</c>.</summary>
    private static int Data2D(BlockFace face) => face switch
    {
        BlockFace.South => 0,
        BlockFace.West => 1,
        BlockFace.North => 2,
        _ => 3
    };
}
