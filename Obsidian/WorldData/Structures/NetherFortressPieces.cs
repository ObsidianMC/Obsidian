using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// The pieces of a nether fortress, like vanilla's <c>NetherFortressPieces</c>. Piece types keep vanilla's names.
/// </summary>
/// <remarks>
/// Bridge pieces (crossings, straights, rooms, the castle entrance) connect to bridges; castle pieces (small corridors,
/// stairs, balconies, the stalk room) connect to castle corridors. Each start piece draws from its own copy of the weight
/// tables, so starts can be built in parallel.
/// </remarks>
public static class NetherFortressPieces
{
    private const int MaxDepth = 30;
    private const int LowestY = 10;
    private const string ChestLoot = "minecraft:chests/nether_bridge";

    private static readonly IBlock air = BlocksRegistry.Air;
    private static readonly IBlock bricks = BlocksRegistry.Get(Material.NetherBricks);
    private static readonly IBlock fence = BlocksRegistry.Get(Material.NetherBrickFence);
    private static readonly IBlock nsFence = Fence("north", "south");
    private static readonly IBlock weFence = Fence("west", "east");
    private static readonly IBlock nseFence = Fence("north", "south", "east");
    private static readonly IBlock nswFence = Fence("north", "south", "west");
    private static readonly IBlock stairs = BlocksRegistry.Get(Material.NetherBrickStairs);

    private static readonly PieceWeight[] bridgePieceWeights =
    [
        new(30, 0, true, BridgeStraight.Create),
        new(10, 4, false, BridgeCrossing.Create),
        new(10, 4, false, RoomCrossing.Create),
        new(10, 3, false, StairsRoom.Create),
        new(5, 2, false, MonsterThrone.Create),
        new(5, 1, false, CastleEntrance.Create)
    ];

    private static readonly PieceWeight[] castlePieceWeights =
    [
        new(25, 0, true, CastleSmallCorridorPiece.Create),
        new(15, 5, false, CastleSmallCorridorCrossingPiece.Create),
        new(5, 10, false, CastleSmallCorridorRightTurnPiece.Create),
        new(5, 10, false, CastleSmallCorridorLeftTurnPiece.Create),
        new(10, 3, true, CastleCorridorStairsPiece.Create),
        new(7, 2, false, CastleCorridorTBalconyPiece.Create),
        new(5, 2, false, CastleStalkRoom.Create)
    ];

    /// <summary>
    /// A nether brick fence connected on the given sides.
    /// </summary>
    private static IBlock Fence(params ReadOnlySpan<string> sides)
    {
        var block = fence;
        foreach (var side in sides)
            block = block.WithProperty(side, true);

        return block;
    }

    private static IBlock Stairs(string facing) => stairs.WithProperty("facing", facing);

    /// <summary>
    /// Vanilla <c>isOkBox</c>: pieces stay above the bottom of the nether.
    /// </summary>
    private static bool IsOkBox(BlockBox box) => box.MinY > LowestY;

    private static bool CanPlace(IStructurePieceAccessor pieces, BlockBox box) => IsOkBox(box) && pieces.FindCollisionPiece(box) is null;

    /// <summary>
    /// Creates a piece with its entrance at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) facing
    /// <paramref name="direction"/>, or returns <c>null</c> when it doesn't fit.
    /// </summary>
    internal delegate NetherBridgePiece? PieceFactory(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z,
        BlockFace direction, int genDepth);

    /// <summary>
    /// Vanilla's <c>PieceWeight</c>: how likely a piece type is picked and how many times it may be placed (0 for unlimited).
    /// </summary>
    internal sealed class PieceWeight(int weight, int maxPlaceCount, bool allowInRow, PieceFactory create)
    {
        public int Weight => weight;

        public int MaxPlaceCount => maxPlaceCount;

        /// <summary>
        /// Whether the type may follow itself.
        /// </summary>
        public bool AllowInRow => allowInRow;

        public PieceFactory Create => create;

        public int PlaceCount { get; set; }

        /// <summary>
        /// Vanilla <c>doPlace</c> and <c>isValid</c>, which are the same check.
        /// </summary>
        public bool CanPlace => maxPlaceCount == 0 || this.PlaceCount < maxPlaceCount;

        public PieceWeight Copy() => new(weight, maxPlaceCount, allowInRow, create);
    }

    /// <summary>
    /// Vanilla's <c>NetherBridgePiece</c>: a fortress piece that grows the fortress from its openings.
    /// </summary>
    public abstract class NetherBridgePiece : StructurePiece
    {
        protected NetherBridgePiece(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox)
        {
            this.Orientation = orientation;
        }

        /// <summary>
        /// Vanilla <c>generateChildForward</c>: grows a piece from the far end, <paramref name="xOffset"/> across.
        /// </summary>
        protected void GenerateChildForward(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int xOffset, int yOffset,
            bool isCastle)
        {
            var box = this.BoundingBox;
            var y = box.MinY + yOffset;
            switch (this.Orientation)
            {
                case BlockFace.North:
                    this.GenerateAndAddPiece(start, pieces, random, box.MinX + xOffset, y, box.MinZ - 1, BlockFace.North, isCastle);
                    break;
                case BlockFace.South:
                    this.GenerateAndAddPiece(start, pieces, random, box.MinX + xOffset, y, box.MaxZ + 1, BlockFace.South, isCastle);
                    break;
                case BlockFace.West:
                    this.GenerateAndAddPiece(start, pieces, random, box.MinX - 1, y, box.MinZ + xOffset, BlockFace.West, isCastle);
                    break;
                case BlockFace.East:
                    this.GenerateAndAddPiece(start, pieces, random, box.MaxX + 1, y, box.MinZ + xOffset, BlockFace.East, isCastle);
                    break;
            }
        }

        /// <summary>
        /// Vanilla <c>generateChildLeft</c>: grows a piece from the west side (north side for pieces facing west or east),
        /// <paramref name="zOffset"/> along it.
        /// </summary>
        protected void GenerateChildLeft(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int yOffset, int zOffset,
            bool isCastle)
        {
            var box = this.BoundingBox;
            var y = box.MinY + yOffset;
            switch (this.Orientation)
            {
                case BlockFace.North or BlockFace.South:
                    this.GenerateAndAddPiece(start, pieces, random, box.MinX - 1, y, box.MinZ + zOffset, BlockFace.West, isCastle);
                    break;
                case BlockFace.West or BlockFace.East:
                    this.GenerateAndAddPiece(start, pieces, random, box.MinX + zOffset, y, box.MinZ - 1, BlockFace.North, isCastle);
                    break;
            }
        }

        /// <summary>
        /// Vanilla <c>generateChildRight</c>: grows a piece from the east side (south side for pieces facing west or east),
        /// <paramref name="zOffset"/> along it.
        /// </summary>
        protected void GenerateChildRight(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int yOffset, int zOffset,
            bool isCastle)
        {
            var box = this.BoundingBox;
            var y = box.MinY + yOffset;
            switch (this.Orientation)
            {
                case BlockFace.North or BlockFace.South:
                    this.GenerateAndAddPiece(start, pieces, random, box.MaxX + 1, y, box.MinZ + zOffset, BlockFace.East, isCastle);
                    break;
                case BlockFace.West or BlockFace.East:
                    this.GenerateAndAddPiece(start, pieces, random, box.MinX + zOffset, y, box.MaxZ + 1, BlockFace.South, isCastle);
                    break;
            }
        }

        /// <summary>
        /// Vanilla <c>generateAndAddPiece</c>: adds a random piece within 112 blocks of the start and queues it to grow.
        /// </summary>
        private void GenerateAndAddPiece(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z,
            BlockFace direction, bool isCastle)
        {
            if (Math.Abs(x - start.BoundingBox.MinX) > 112 || Math.Abs(z - start.BoundingBox.MinZ) > 112)
            {
                // Vanilla creates an end filler here without adding it; creating it still draws its seed.
                BridgeEndFiller.Create(pieces, random, x, y, z, direction, this.GenDepth);
                return;
            }

            var available = isCastle ? start.AvailableCastlePieces : start.AvailableBridgePieces;
            var piece = GeneratePiece(start, available, pieces, random, x, y, z, direction, this.GenDepth + 1);
            if (piece is null)
                return;

            pieces.AddPiece(piece);
            start.PendingChildren.Add(piece);
        }

        /// <summary>
        /// Vanilla <c>generatePiece</c>: up to 5 weighted picks, falling back to an end filler.
        /// </summary>
        private static NetherBridgePiece? GeneratePiece(StartPiece start, List<PieceWeight> available, IStructurePieceAccessor pieces,
            IRandomSource random, int x, int y, int z, BlockFace direction, int depth)
        {
            var totalWeight = TotalWeight(available);
            if (totalWeight > 0 && depth <= MaxDepth)
            {
                for (var attempt = 0; attempt < 5; attempt++)
                {
                    var selection = random.NextInt(totalWeight);
                    foreach (var weight in available)
                    {
                        selection -= weight.Weight;
                        if (selection >= 0)
                            continue;

                        if (!weight.CanPlace || weight == start.PreviousPiece && !weight.AllowInRow)
                            break;

                        // Like vanilla, a type that doesn't fit lets the following types try in turn.
                        var piece = weight.Create(pieces, random, x, y, z, direction, depth);
                        if (piece is null)
                            continue;

                        weight.PlaceCount++;
                        start.PreviousPiece = weight;
                        if (!weight.CanPlace)
                            available.Remove(weight);

                        return piece;
                    }
                }
            }

            return BridgeEndFiller.Create(pieces, random, x, y, z, direction, depth);
        }

        /// <summary>
        /// Vanilla <c>updatePieceWeight</c>: the total weight, or -1 once every limited type is used up.
        /// </summary>
        private static int TotalWeight(List<PieceWeight> available)
        {
            var hasLimitedPieces = false;
            var total = 0;
            foreach (var weight in available)
            {
                if (weight.MaxPlaceCount > 0 && weight.PlaceCount < weight.MaxPlaceCount)
                    hasLimitedPieces = true;

                total += weight.Weight;
            }

            return hasLimitedPieces ? total : -1;
        }

        protected void Fill(StructurePieceContext context, int minX, int minY, int minZ, int maxX, int maxY, int maxZ, IBlock block) =>
            this.GenerateBox(context.Level, context.Box, minX, minY, minZ, maxX, maxY, maxZ, block, block, false);

        protected void Place(StructurePieceContext context, IBlock block, int x, int y, int z) =>
            this.PlaceBlock(context.Level, block, x, y, z, context.Box);

        /// <summary>
        /// Fills nether bricks down from below the piece's floor at local (<paramref name="x"/>, <paramref name="z"/>).
        /// </summary>
        protected void FillBricksDown(StructurePieceContext context, int x, int z) =>
            this.FillColumnDown(context.Level, bricks, x, -1, z, context.Box);

        /// <summary>
        /// The 13x13 castle room shell shared by the entrance and the stalk room: floor, walls, roof, fenced windows and the
        /// fence parapet.
        /// </summary>
        protected void GenerateCastleRoomShell(StructurePieceContext context)
        {
            this.Fill(context, 0, 3, 0, 12, 4, 12, bricks);
            this.Fill(context, 0, 5, 0, 12, 13, 12, air);
            this.Fill(context, 0, 5, 0, 1, 12, 12, bricks);
            this.Fill(context, 11, 5, 0, 12, 12, 12, bricks);
            this.Fill(context, 2, 5, 11, 4, 12, 12, bricks);
            this.Fill(context, 8, 5, 11, 10, 12, 12, bricks);
            this.Fill(context, 5, 9, 11, 7, 12, 12, bricks);
            this.Fill(context, 2, 5, 0, 4, 12, 1, bricks);
            this.Fill(context, 8, 5, 0, 10, 12, 1, bricks);
            this.Fill(context, 5, 9, 0, 7, 12, 1, bricks);
            this.Fill(context, 2, 11, 2, 10, 12, 10, bricks);
        }

        /// <summary>
        /// The fenced parapet on top of the castle rooms.
        /// </summary>
        protected void GenerateCastleRoomParapet(StructurePieceContext context)
        {
            for (var i = 1; i <= 11; i += 2)
            {
                this.Fill(context, i, 10, 0, i, 11, 0, weFence);
                this.Fill(context, i, 10, 12, i, 11, 12, weFence);
                this.Fill(context, 0, 10, i, 0, 11, i, nsFence);
                this.Fill(context, 12, 10, i, 12, 11, i, nsFence);
                this.Place(context, bricks, i, 13, 0);
                this.Place(context, bricks, i, 13, 12);
                this.Place(context, bricks, 0, 13, i);
                this.Place(context, bricks, 12, 13, i);
                if (i != 11)
                {
                    this.Place(context, weFence, i + 1, 13, 0);
                    this.Place(context, weFence, i + 1, 13, 12);
                    this.Place(context, nsFence, 0, 13, i + 1);
                    this.Place(context, nsFence, 12, 13, i + 1);
                }
            }

            this.Place(context, Fence("north", "east"), 0, 13, 0);
            this.Place(context, Fence("south", "east"), 0, 13, 12);
            this.Place(context, Fence("south", "west"), 12, 13, 12);
            this.Place(context, Fence("north", "west"), 12, 13, 0);
        }

        /// <summary>
        /// The bridge decks leading into the castle rooms and the pillars below them.
        /// </summary>
        protected void GenerateCastleRoomBridges(StructurePieceContext context)
        {
            this.Fill(context, 4, 2, 0, 8, 2, 12, bricks);
            this.Fill(context, 0, 2, 4, 12, 2, 8, bricks);
            this.Fill(context, 4, 0, 0, 8, 1, 3, bricks);
            this.Fill(context, 4, 0, 9, 8, 1, 12, bricks);
            this.Fill(context, 0, 0, 4, 3, 1, 8, bricks);
            this.Fill(context, 9, 0, 4, 12, 1, 8, bricks);

            for (var x = 4; x <= 8; x++)
            {
                for (var z = 0; z <= 2; z++)
                {
                    this.FillBricksDown(context, x, z);
                    this.FillBricksDown(context, x, 12 - z);
                }
            }

            for (var x = 0; x <= 2; x++)
            {
                for (var z = 4; z <= 8; z++)
                {
                    this.FillBricksDown(context, x, z);
                    this.FillBricksDown(context, 12 - x, z);
                }
            }
        }

        /// <summary>
        /// The floor, open space and roof of a 5x7x5 castle corridor.
        /// </summary>
        protected void GenerateSmallCorridorShell(StructurePieceContext context)
        {
            this.Fill(context, 0, 0, 0, 4, 1, 4, bricks);
            this.Fill(context, 0, 2, 0, 4, 5, 4, air);
        }

        /// <summary>
        /// The roof of a small castle corridor and its pillars.
        /// </summary>
        protected void GenerateSmallCorridorRoofAndPillars(StructurePieceContext context)
        {
            this.Fill(context, 0, 6, 0, 4, 6, 4, bricks);

            for (var x = 0; x <= 4; x++)
            {
                for (var z = 0; z <= 4; z++)
                    this.FillBricksDown(context, x, z);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>BridgeCrossing</c>: a 19x19 crossing of two bridges.
    /// </summary>
    public class BridgeCrossing : NetherBridgePiece
    {
        public BridgeCrossing(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        /// <summary>
        /// The crossing at the fortress's start.
        /// </summary>
        protected BridgeCrossing(int west, int north, BlockFace orientation)
            : base(0, MakeBoundingBox(west, 64, north, orientation, 19, 10, 19), orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -8, -3, 0, 19, 10, 19, direction);
            return CanPlace(pieces, box) ? new BridgeCrossing(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var fortress = (StartPiece)start;
            this.GenerateChildForward(fortress, pieces, random, 8, 3, false);
            this.GenerateChildLeft(fortress, pieces, random, 3, 8, false);
            this.GenerateChildRight(fortress, pieces, random, 3, 8, false);
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.Fill(context, 7, 3, 0, 11, 4, 18, bricks);
            this.Fill(context, 0, 3, 7, 18, 4, 11, bricks);
            this.Fill(context, 8, 5, 0, 10, 7, 18, air);
            this.Fill(context, 0, 5, 8, 18, 7, 10, air);
            this.Fill(context, 7, 5, 0, 7, 5, 7, bricks);
            this.Fill(context, 7, 5, 11, 7, 5, 18, bricks);
            this.Fill(context, 11, 5, 0, 11, 5, 7, bricks);
            this.Fill(context, 11, 5, 11, 11, 5, 18, bricks);
            this.Fill(context, 0, 5, 7, 7, 5, 7, bricks);
            this.Fill(context, 11, 5, 7, 18, 5, 7, bricks);
            this.Fill(context, 0, 5, 11, 7, 5, 11, bricks);
            this.Fill(context, 11, 5, 11, 18, 5, 11, bricks);
            this.Fill(context, 7, 2, 0, 11, 2, 5, bricks);
            this.Fill(context, 7, 2, 13, 11, 2, 18, bricks);
            this.Fill(context, 7, 0, 0, 11, 1, 3, bricks);
            this.Fill(context, 7, 0, 15, 11, 1, 18, bricks);

            for (var x = 7; x <= 11; x++)
            {
                for (var z = 0; z <= 2; z++)
                {
                    this.FillBricksDown(context, x, z);
                    this.FillBricksDown(context, x, 18 - z);
                }
            }

            this.Fill(context, 0, 2, 7, 5, 2, 11, bricks);
            this.Fill(context, 13, 2, 7, 18, 2, 11, bricks);
            this.Fill(context, 0, 0, 7, 3, 1, 11, bricks);
            this.Fill(context, 15, 0, 7, 18, 1, 11, bricks);

            for (var x = 0; x <= 2; x++)
            {
                for (var z = 7; z <= 11; z++)
                {
                    this.FillBricksDown(context, x, z);
                    this.FillBricksDown(context, 18 - x, z);
                }
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>StartPiece</c>: the bridge crossing the fortress grows from, holding the build state.
    /// </summary>
    public sealed class StartPiece : BridgeCrossing
    {
        public StartPiece(IRandomSource random, int west, int north) : base(west, north, RandomHorizontalDirection(random))
        {
        }

        /// <summary>
        /// The last piece type placed, which most types can't directly follow.
        /// </summary>
        internal PieceWeight? PreviousPiece { get; set; }

        internal List<PieceWeight> AvailableBridgePieces { get; } = [.. bridgePieceWeights.Select(weight => weight.Copy())];

        internal List<PieceWeight> AvailableCastlePieces { get; } = [.. castlePieceWeights.Select(weight => weight.Copy())];

        /// <summary>
        /// Pieces added but not grown yet.
        /// </summary>
        public List<StructurePiece> PendingChildren { get; } = [];
    }

    /// <summary>
    /// Vanilla's <c>BridgeEndFiller</c>: the crumbling end of a bridge that leads nowhere.
    /// </summary>
    public sealed class BridgeEndFiller : NetherBridgePiece
    {
        private readonly int selfSeed;

        private BridgeEndFiller(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace orientation)
            : base(genDepth, boundingBox, orientation)
        {
            this.selfSeed = random.NextInt();
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -3, 0, 5, 10, 8, direction);
            return CanPlace(pieces, box) ? new BridgeEndFiller(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            // The ragged edge comes from the piece's own seed, so it's the same in every chunk.
            var random = new LegacyRandomSource(this.selfSeed);

            for (var x = 0; x <= 4; x++)
            {
                for (var y = 3; y <= 4; y++)
                    this.Fill(context, x, y, 0, x, y, random.NextInt(8), bricks);
            }

            this.Fill(context, 0, 5, 0, 0, 5, random.NextInt(8), bricks);
            this.Fill(context, 4, 5, 0, 4, 5, random.NextInt(8), bricks);

            for (var x = 0; x <= 4; x++)
                this.Fill(context, x, 2, 0, x, 2, random.NextInt(5), bricks);

            for (var x = 0; x <= 4; x++)
            {
                for (var y = 0; y <= 1; y++)
                    this.Fill(context, x, y, 0, x, y, random.NextInt(3), bricks);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>BridgeStraight</c>: a 19 block long fenced bridge.
    /// </summary>
    public sealed class BridgeStraight : NetherBridgePiece
    {
        private BridgeStraight(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -3, 0, 5, 10, 19, direction);
            return CanPlace(pieces, box) ? new BridgeStraight(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateChildForward((StartPiece)start, pieces, random, 1, 3, false);

        public override void PostProcess(StructurePieceContext context)
        {
            this.Fill(context, 0, 3, 0, 4, 4, 18, bricks);
            this.Fill(context, 1, 5, 0, 3, 7, 18, air);
            this.Fill(context, 0, 5, 0, 0, 5, 18, bricks);
            this.Fill(context, 4, 5, 0, 4, 5, 18, bricks);
            this.Fill(context, 0, 2, 0, 4, 2, 5, bricks);
            this.Fill(context, 0, 2, 13, 4, 2, 18, bricks);
            this.Fill(context, 0, 0, 0, 4, 1, 3, bricks);
            this.Fill(context, 0, 0, 15, 4, 1, 18, bricks);

            for (var x = 0; x <= 4; x++)
            {
                for (var z = 0; z <= 2; z++)
                {
                    this.FillBricksDown(context, x, z);
                    this.FillBricksDown(context, x, 18 - z);
                }
            }

            this.Fill(context, 0, 1, 1, 0, 4, 1, nseFence);
            this.Fill(context, 0, 3, 4, 0, 4, 4, nseFence);
            this.Fill(context, 0, 3, 14, 0, 4, 14, nseFence);
            this.Fill(context, 0, 1, 17, 0, 4, 17, nseFence);
            this.Fill(context, 4, 1, 1, 4, 4, 1, nswFence);
            this.Fill(context, 4, 3, 4, 4, 4, 4, nswFence);
            this.Fill(context, 4, 3, 14, 4, 4, 14, nswFence);
            this.Fill(context, 4, 1, 17, 4, 4, 17, nswFence);
        }
    }

    /// <summary>
    /// Vanilla's <c>CastleCorridorStairsPiece</c>: a castle corridor stairway going down.
    /// </summary>
    public sealed class CastleCorridorStairsPiece : NetherBridgePiece
    {
        private CastleCorridorStairsPiece(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -7, 0, 5, 14, 10, direction);
            return CanPlace(pieces, box) ? new CastleCorridorStairsPiece(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateChildForward((StartPiece)start, pieces, random, 1, 0, true);

        public override void PostProcess(StructurePieceContext context)
        {
            var southStairs = Stairs("south");

            for (var z = 0; z <= 9; z++)
            {
                var floor = Math.Max(1, 7 - z);
                var roof = Math.Min(Math.Max(floor + 5, 14 - z), 13);
                this.Fill(context, 0, 0, z, 4, floor, z, bricks);
                this.Fill(context, 1, floor + 1, z, 3, roof - 1, z, air);
                if (z <= 6)
                {
                    this.Place(context, southStairs, 1, floor + 1, z);
                    this.Place(context, southStairs, 2, floor + 1, z);
                    this.Place(context, southStairs, 3, floor + 1, z);
                }

                this.Fill(context, 0, roof, z, 4, roof, z, bricks);
                this.Fill(context, 0, floor + 1, z, 0, roof - 1, z, bricks);
                this.Fill(context, 4, floor + 1, z, 4, roof - 1, z, bricks);
                if ((z & 1) == 0)
                {
                    this.Fill(context, 0, floor + 2, z, 0, floor + 3, z, nsFence);
                    this.Fill(context, 4, floor + 2, z, 4, floor + 3, z, nsFence);
                }

                for (var x = 0; x <= 4; x++)
                    this.FillBricksDown(context, x, z);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>CastleCorridorTBalconyPiece</c>: a corridor junction with a balcony.
    /// </summary>
    public sealed class CastleCorridorTBalconyPiece : NetherBridgePiece
    {
        private CastleCorridorTBalconyPiece(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -3, 0, 0, 9, 7, 9, direction);
            return CanPlace(pieces, box) ? new CastleCorridorTBalconyPiece(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var fortress = (StartPiece)start;
            var zOffset = this.Orientation is BlockFace.West or BlockFace.North ? 5 : 1;
            this.GenerateChildLeft(fortress, pieces, random, 0, zOffset, random.NextInt(8) > 0);
            this.GenerateChildRight(fortress, pieces, random, 0, zOffset, random.NextInt(8) > 0);
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.Fill(context, 0, 0, 0, 8, 1, 8, bricks);
            this.Fill(context, 0, 2, 0, 8, 5, 8, air);
            this.Fill(context, 0, 6, 0, 8, 6, 5, bricks);
            this.Fill(context, 0, 2, 0, 2, 5, 0, bricks);
            this.Fill(context, 6, 2, 0, 8, 5, 0, bricks);
            this.Fill(context, 1, 3, 0, 1, 4, 0, weFence);
            this.Fill(context, 7, 3, 0, 7, 4, 0, weFence);
            this.Fill(context, 0, 2, 4, 8, 2, 8, bricks);
            this.Fill(context, 1, 1, 4, 2, 2, 4, air);
            this.Fill(context, 6, 1, 4, 7, 2, 4, air);
            this.Fill(context, 1, 3, 8, 7, 3, 8, weFence);
            this.Place(context, Fence("east", "south"), 0, 3, 8);
            this.Place(context, Fence("west", "south"), 8, 3, 8);
            this.Fill(context, 0, 3, 6, 0, 3, 7, nsFence);
            this.Fill(context, 8, 3, 6, 8, 3, 7, nsFence);
            this.Fill(context, 0, 3, 4, 0, 5, 5, bricks);
            this.Fill(context, 8, 3, 4, 8, 5, 5, bricks);
            this.Fill(context, 1, 3, 5, 2, 5, 5, bricks);
            this.Fill(context, 6, 3, 5, 7, 5, 5, bricks);
            this.Fill(context, 1, 4, 5, 1, 5, 5, weFence);
            this.Fill(context, 7, 4, 5, 7, 5, 5, weFence);

            for (var z = 0; z <= 5; z++)
            {
                for (var x = 0; x <= 8; x++)
                    this.FillBricksDown(context, x, z);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>CastleEntrance</c>: the castle gate room with a lava well.
    /// </summary>
    public sealed class CastleEntrance : NetherBridgePiece
    {
        private CastleEntrance(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -5, -3, 0, 13, 14, 13, direction);
            return CanPlace(pieces, box) ? new CastleEntrance(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateChildForward((StartPiece)start, pieces, random, 5, 3, true);

        public override void PostProcess(StructurePieceContext context)
        {
            this.GenerateCastleRoomShell(context);
            this.Fill(context, 5, 8, 0, 7, 8, 0, fence);
            this.GenerateCastleRoomParapet(context);

            for (var z = 3; z <= 9; z += 2)
            {
                this.Fill(context, 1, 7, z, 1, 8, z, nswFence);
                this.Fill(context, 11, 7, z, 11, 8, z, nseFence);
            }

            this.GenerateCastleRoomBridges(context);

            this.Fill(context, 5, 5, 5, 7, 5, 7, bricks);
            this.Fill(context, 6, 1, 6, 6, 4, 6, air);
            this.Place(context, bricks, 6, 0, 6);

            // Placing the lava schedules its tick, which vanilla schedules a second time here.
            this.Place(context, BlocksRegistry.Get(Material.Lava), 6, 5, 6);
        }
    }

    /// <summary>
    /// Vanilla's <c>CastleSmallCorridorCrossingPiece</c>: a four-way corridor junction.
    /// </summary>
    public sealed class CastleSmallCorridorCrossingPiece : NetherBridgePiece
    {
        private CastleSmallCorridorCrossingPiece(int genDepth, BlockBox boundingBox, BlockFace orientation)
            : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, 0, 0, 5, 7, 5, direction);
            return CanPlace(pieces, box) ? new CastleSmallCorridorCrossingPiece(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var fortress = (StartPiece)start;
            this.GenerateChildForward(fortress, pieces, random, 1, 0, true);
            this.GenerateChildLeft(fortress, pieces, random, 0, 1, true);
            this.GenerateChildRight(fortress, pieces, random, 0, 1, true);
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.GenerateSmallCorridorShell(context);
            this.Fill(context, 0, 2, 0, 0, 5, 0, bricks);
            this.Fill(context, 4, 2, 0, 4, 5, 0, bricks);
            this.Fill(context, 0, 2, 4, 0, 5, 4, bricks);
            this.Fill(context, 4, 2, 4, 4, 5, 4, bricks);
            this.GenerateSmallCorridorRoofAndPillars(context);
        }
    }

    /// <summary>
    /// Vanilla's <c>CastleSmallCorridorLeftTurnPiece</c>: a corridor turning west (north when facing west or east),
    /// sometimes with a chest.
    /// </summary>
    public sealed class CastleSmallCorridorLeftTurnPiece : NetherBridgePiece
    {
        private bool isNeedingChest;

        private CastleSmallCorridorLeftTurnPiece(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace orientation)
            : base(genDepth, boundingBox, orientation)
        {
            this.isNeedingChest = random.NextInt(3) == 0;
        }

        internal override void SaveState(NbtCompound tag)
        {
            base.SaveState(tag);

            tag.Add(new NbtTag<bool>("Chest", this.isNeedingChest));
        }

        internal override void LoadState(NbtCompound tag)
        {
            base.LoadState(tag);

            this.isNeedingChest = tag.TryGetBool("Chest", out var needsChest) && needsChest;
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, 0, 0, 5, 7, 5, direction);
            return CanPlace(pieces, box) ? new CastleSmallCorridorLeftTurnPiece(genDepth, random, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateChildLeft((StartPiece)start, pieces, random, 0, 1, true);

        public override void PostProcess(StructurePieceContext context)
        {
            this.GenerateSmallCorridorShell(context);
            this.Fill(context, 4, 2, 0, 4, 5, 4, bricks);
            this.Fill(context, 4, 3, 1, 4, 4, 1, nsFence);
            this.Fill(context, 4, 3, 3, 4, 4, 3, nsFence);
            this.Fill(context, 0, 2, 0, 0, 5, 0, bricks);
            this.Fill(context, 0, 2, 4, 3, 5, 4, bricks);
            this.Fill(context, 1, 3, 4, 1, 4, 4, weFence);
            this.Fill(context, 3, 3, 4, 3, 4, 4, weFence);

            // The chest is placed once, with the chunk that holds its spot.
            if (this.isNeedingChest && context.Box.IsInside(this.GetWorldPos(3, 2, 3)))
            {
                this.isNeedingChest = false;
                this.CreateChest(context.Level, context.Box, context.Random, 3, 2, 3, ChestLoot);
            }

            this.GenerateSmallCorridorRoofAndPillars(context);
        }
    }

    /// <summary>
    /// Vanilla's <c>CastleSmallCorridorPiece</c>: a straight castle corridor.
    /// </summary>
    public sealed class CastleSmallCorridorPiece : NetherBridgePiece
    {
        private CastleSmallCorridorPiece(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, 0, 0, 5, 7, 5, direction);
            return CanPlace(pieces, box) ? new CastleSmallCorridorPiece(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateChildForward((StartPiece)start, pieces, random, 1, 0, true);

        public override void PostProcess(StructurePieceContext context)
        {
            this.GenerateSmallCorridorShell(context);
            this.Fill(context, 0, 2, 0, 0, 5, 4, bricks);
            this.Fill(context, 4, 2, 0, 4, 5, 4, bricks);
            this.Fill(context, 0, 3, 1, 0, 4, 1, nsFence);
            this.Fill(context, 0, 3, 3, 0, 4, 3, nsFence);
            this.Fill(context, 4, 3, 1, 4, 4, 1, nsFence);
            this.Fill(context, 4, 3, 3, 4, 4, 3, nsFence);
            this.GenerateSmallCorridorRoofAndPillars(context);
        }
    }

    /// <summary>
    /// Vanilla's <c>CastleSmallCorridorRightTurnPiece</c>: a corridor turning east (south when facing west or east),
    /// sometimes with a chest.
    /// </summary>
    public sealed class CastleSmallCorridorRightTurnPiece : NetherBridgePiece
    {
        private bool isNeedingChest;

        private CastleSmallCorridorRightTurnPiece(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace orientation)
            : base(genDepth, boundingBox, orientation)
        {
            this.isNeedingChest = random.NextInt(3) == 0;
        }

        internal override void SaveState(NbtCompound tag)
        {
            base.SaveState(tag);

            tag.Add(new NbtTag<bool>("Chest", this.isNeedingChest));
        }

        internal override void LoadState(NbtCompound tag)
        {
            base.LoadState(tag);

            this.isNeedingChest = tag.TryGetBool("Chest", out var needsChest) && needsChest;
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, 0, 0, 5, 7, 5, direction);
            return CanPlace(pieces, box) ? new CastleSmallCorridorRightTurnPiece(genDepth, random, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateChildRight((StartPiece)start, pieces, random, 0, 1, true);

        public override void PostProcess(StructurePieceContext context)
        {
            this.GenerateSmallCorridorShell(context);
            this.Fill(context, 0, 2, 0, 0, 5, 4, bricks);
            this.Fill(context, 0, 3, 1, 0, 4, 1, nsFence);
            this.Fill(context, 0, 3, 3, 0, 4, 3, nsFence);
            this.Fill(context, 4, 2, 0, 4, 5, 0, bricks);
            this.Fill(context, 1, 2, 4, 4, 5, 4, bricks);
            this.Fill(context, 1, 3, 4, 1, 4, 4, weFence);
            this.Fill(context, 3, 3, 4, 3, 4, 4, weFence);

            // The chest is placed once, with the chunk that holds its spot.
            if (this.isNeedingChest && context.Box.IsInside(this.GetWorldPos(1, 2, 3)))
            {
                this.isNeedingChest = false;
                this.CreateChest(context.Level, context.Box, context.Random, 1, 2, 3, ChestLoot);
            }

            this.GenerateSmallCorridorRoofAndPillars(context);
        }
    }

    /// <summary>
    /// Vanilla's <c>CastleStalkRoom</c>: the castle room with nether wart beds and a staircase.
    /// </summary>
    public sealed class CastleStalkRoom : NetherBridgePiece
    {
        private CastleStalkRoom(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -5, -3, 0, 13, 14, 13, direction);
            return CanPlace(pieces, box) ? new CastleStalkRoom(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var fortress = (StartPiece)start;
            this.GenerateChildForward(fortress, pieces, random, 5, 3, true);
            this.GenerateChildForward(fortress, pieces, random, 5, 11, true);
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.GenerateCastleRoomShell(context);
            this.GenerateCastleRoomParapet(context);

            for (var z = 3; z <= 9; z += 2)
            {
                this.Fill(context, 1, 7, z, 1, 8, z, nswFence);
                this.Fill(context, 11, 7, z, 11, 8, z, nseFence);
            }

            var northStairs = Stairs("north");
            for (var i = 0; i <= 6; i++)
            {
                var z = i + 4;
                for (var x = 5; x <= 7; x++)
                    this.Place(context, northStairs, x, 5 + i, z);

                if (z is >= 5 and <= 8)
                    this.Fill(context, 5, 5, z, 7, i + 4, z, bricks);
                else if (z is >= 9 and <= 10)
                    this.Fill(context, 5, 8, z, 7, i + 4, z, bricks);

                if (i >= 1)
                    this.Fill(context, 5, 6 + i, z, 7, 9 + i, z, air);
            }

            for (var x = 5; x <= 7; x++)
                this.Place(context, northStairs, x, 12, 11);

            this.Fill(context, 5, 6, 7, 5, 7, 7, nseFence);
            this.Fill(context, 7, 6, 7, 7, 7, 7, nswFence);
            this.Fill(context, 5, 13, 12, 7, 13, 12, air);
            this.Fill(context, 2, 5, 2, 3, 5, 3, bricks);
            this.Fill(context, 2, 5, 9, 3, 5, 10, bricks);
            this.Fill(context, 2, 5, 4, 2, 5, 8, bricks);
            this.Fill(context, 9, 5, 2, 10, 5, 3, bricks);
            this.Fill(context, 9, 5, 9, 10, 5, 10, bricks);
            this.Fill(context, 10, 5, 4, 10, 5, 8, bricks);

            var eastStairs = Stairs("east");
            var westStairs = Stairs("west");
            this.Place(context, westStairs, 4, 5, 2);
            this.Place(context, westStairs, 4, 5, 3);
            this.Place(context, westStairs, 4, 5, 9);
            this.Place(context, westStairs, 4, 5, 10);
            this.Place(context, eastStairs, 8, 5, 2);
            this.Place(context, eastStairs, 8, 5, 3);
            this.Place(context, eastStairs, 8, 5, 9);
            this.Place(context, eastStairs, 8, 5, 10);

            var soulSand = BlocksRegistry.Get(Material.SoulSand);
            var netherWart = BlocksRegistry.Get(Material.NetherWart);
            this.Fill(context, 3, 4, 4, 4, 4, 8, soulSand);
            this.Fill(context, 8, 4, 4, 9, 4, 8, soulSand);
            this.Fill(context, 3, 5, 4, 4, 5, 8, netherWart);
            this.Fill(context, 8, 5, 4, 9, 5, 8, netherWart);

            this.GenerateCastleRoomBridges(context);
        }
    }

    /// <summary>
    /// Vanilla's <c>MonsterThrone</c>: a raised platform with a blaze spawner.
    /// </summary>
    public sealed class MonsterThrone : NetherBridgePiece
    {
        private bool hasPlacedSpawner;

        private MonsterThrone(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal override void SaveState(NbtCompound tag)
        {
            base.SaveState(tag);

            tag.Add(new NbtTag<bool>("Mob", this.hasPlacedSpawner));
        }

        internal override void LoadState(NbtCompound tag)
        {
            base.LoadState(tag);

            this.hasPlacedSpawner = tag.TryGetBool("Mob", out var placed) && placed;
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -2, 0, 0, 7, 8, 9, direction);
            return CanPlace(pieces, box) ? new MonsterThrone(genDepth, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.Fill(context, 0, 2, 0, 6, 7, 7, air);
            this.Fill(context, 1, 0, 0, 5, 1, 7, bricks);
            this.Fill(context, 1, 2, 1, 5, 2, 7, bricks);
            this.Fill(context, 1, 3, 2, 5, 3, 7, bricks);
            this.Fill(context, 1, 4, 3, 5, 4, 7, bricks);
            this.Fill(context, 1, 2, 0, 1, 4, 2, bricks);
            this.Fill(context, 5, 2, 0, 5, 4, 2, bricks);
            this.Fill(context, 1, 5, 2, 1, 5, 3, bricks);
            this.Fill(context, 5, 5, 2, 5, 5, 3, bricks);
            this.Fill(context, 0, 5, 3, 0, 5, 8, bricks);
            this.Fill(context, 6, 5, 3, 6, 5, 8, bricks);
            this.Fill(context, 1, 5, 8, 5, 5, 8, bricks);
            this.Place(context, Fence("west"), 1, 6, 3);
            this.Place(context, Fence("east"), 5, 6, 3);
            this.Place(context, Fence("east", "north"), 0, 6, 3);
            this.Place(context, Fence("west", "north"), 6, 6, 3);
            this.Fill(context, 0, 6, 4, 0, 6, 7, nsFence);
            this.Fill(context, 6, 6, 4, 6, 6, 7, nsFence);
            this.Place(context, Fence("east", "south"), 0, 6, 8);
            this.Place(context, Fence("west", "south"), 6, 6, 8);
            this.Fill(context, 1, 6, 8, 5, 6, 8, weFence);
            this.Place(context, Fence("east"), 1, 7, 8);
            this.Fill(context, 2, 7, 8, 4, 7, 8, weFence);
            this.Place(context, Fence("west"), 5, 7, 8);
            this.Place(context, Fence("east"), 2, 8, 8);
            this.Place(context, weFence, 3, 8, 8);
            this.Place(context, Fence("west"), 4, 8, 8);

            // The spawner is placed once, with the chunk that holds its spot.
            if (!this.hasPlacedSpawner)
            {
                var position = this.GetWorldPos(3, 5, 5);
                if (context.Box.IsInside(position))
                {
                    this.hasPlacedSpawner = true;
                    context.Level.SetBlock(position, BlocksRegistry.Get(Material.Spawner));
                    FeatureHelpers.SetSpawnerEntity(context.Level, position, () => "minecraft:blaze");
                }
            }

            for (var x = 0; x <= 6; x++)
            {
                for (var z = 0; z <= 6; z++)
                    this.FillBricksDown(context, x, z);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>RoomCrossing</c>: a small four-way room on the bridges.
    /// </summary>
    public sealed class RoomCrossing : NetherBridgePiece
    {
        private RoomCrossing(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -2, 0, 0, 7, 9, 7, direction);
            return CanPlace(pieces, box) ? new RoomCrossing(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var fortress = (StartPiece)start;
            this.GenerateChildForward(fortress, pieces, random, 2, 0, false);
            this.GenerateChildLeft(fortress, pieces, random, 0, 2, false);
            this.GenerateChildRight(fortress, pieces, random, 0, 2, false);
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.Fill(context, 0, 0, 0, 6, 1, 6, bricks);
            this.Fill(context, 0, 2, 0, 6, 7, 6, air);
            this.Fill(context, 0, 2, 0, 1, 6, 0, bricks);
            this.Fill(context, 0, 2, 6, 1, 6, 6, bricks);
            this.Fill(context, 5, 2, 0, 6, 6, 0, bricks);
            this.Fill(context, 5, 2, 6, 6, 6, 6, bricks);
            this.Fill(context, 0, 2, 0, 0, 6, 1, bricks);
            this.Fill(context, 0, 2, 5, 0, 6, 6, bricks);
            this.Fill(context, 6, 2, 0, 6, 6, 1, bricks);
            this.Fill(context, 6, 2, 5, 6, 6, 6, bricks);
            this.Fill(context, 2, 6, 0, 4, 6, 0, bricks);
            this.Fill(context, 2, 5, 0, 4, 5, 0, weFence);
            this.Fill(context, 2, 6, 6, 4, 6, 6, bricks);
            this.Fill(context, 2, 5, 6, 4, 5, 6, weFence);
            this.Fill(context, 0, 6, 2, 0, 6, 4, bricks);
            this.Fill(context, 0, 5, 2, 0, 5, 4, nsFence);
            this.Fill(context, 6, 6, 2, 6, 6, 4, bricks);
            this.Fill(context, 6, 5, 2, 6, 5, 4, nsFence);

            for (var x = 0; x <= 6; x++)
            {
                for (var z = 0; z <= 6; z++)
                    this.FillBricksDown(context, x, z);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>StairsRoom</c>: a room with stairs up to an exit on its east side (south when facing west or east).
    /// </summary>
    public sealed class StairsRoom : NetherBridgePiece
    {
        private StairsRoom(int genDepth, BlockBox boundingBox, BlockFace orientation) : base(genDepth, boundingBox, orientation)
        {
        }

        internal static NetherBridgePiece? Create(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -2, 0, 0, 7, 11, 7, direction);
            return CanPlace(pieces, box) ? new StairsRoom(genDepth, box, direction) : null;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateChildRight((StartPiece)start, pieces, random, 6, 2, false);

        public override void PostProcess(StructurePieceContext context)
        {
            this.Fill(context, 0, 0, 0, 6, 1, 6, bricks);
            this.Fill(context, 0, 2, 0, 6, 10, 6, air);
            this.Fill(context, 0, 2, 0, 1, 8, 0, bricks);
            this.Fill(context, 5, 2, 0, 6, 8, 0, bricks);
            this.Fill(context, 0, 2, 1, 0, 8, 6, bricks);
            this.Fill(context, 6, 2, 1, 6, 8, 6, bricks);
            this.Fill(context, 1, 2, 6, 5, 8, 6, bricks);
            this.Fill(context, 0, 3, 2, 0, 5, 4, nsFence);
            this.Fill(context, 6, 3, 2, 6, 5, 2, nsFence);
            this.Fill(context, 6, 3, 4, 6, 5, 4, nsFence);
            this.Place(context, bricks, 5, 2, 5);
            this.Fill(context, 4, 2, 5, 4, 3, 5, bricks);
            this.Fill(context, 3, 2, 5, 3, 4, 5, bricks);
            this.Fill(context, 2, 2, 5, 2, 5, 5, bricks);
            this.Fill(context, 1, 2, 5, 1, 6, 5, bricks);
            this.Fill(context, 1, 7, 1, 5, 7, 4, bricks);
            this.Fill(context, 6, 8, 2, 6, 8, 4, air);
            this.Fill(context, 2, 6, 0, 4, 8, 0, bricks);
            this.Fill(context, 2, 5, 0, 4, 5, 0, weFence);

            for (var x = 0; x <= 6; x++)
            {
                for (var z = 0; z <= 6; z++)
                    this.FillBricksDown(context, x, z);
            }
        }
    }
}
