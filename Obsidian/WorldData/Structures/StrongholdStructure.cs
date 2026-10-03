using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Strongholds: corridors and rooms branching out of a spiral staircase down to the end portal room, like vanilla's
/// <c>StrongholdStructure</c>.
/// </summary>
[StructureType("minecraft:stronghold")]
public sealed class StrongholdStructure : Structure
{
    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context) =>
        new(new Vector(context.ChunkX << 4, 0, context.ChunkZ << 4), builder => GeneratePieces(builder, context));

    private static void GeneratePieces(StructurePiecesBuilder builder, StructureGenerationContext context)
    {
        var random = context.Random;
        var tries = 0;
        StrongholdPieces.StartPiece start;

        // Like vanilla, retries with the next seed until the stronghold has a portal room.
        do
        {
            builder.Clear();
            random.SetLargeFeatureSeed(context.Seed + tries++, context.ChunkX, context.ChunkZ);
            start = new StrongholdPieces.StartPiece(random, (context.ChunkX << 4) + 2, (context.ChunkZ << 4) + 2);
            builder.AddPiece(start);
            start.AddChildren(start, builder, random);

            var pending = start.PendingChildren;
            while (pending.Count > 0)
            {
                var index = random.NextInt(pending.Count);
                var piece = pending[index];
                pending.RemoveAt(index);
                piece.AddChildren(start, builder, random);
            }

            builder.MoveBelowSeaLevel(context.Terrain.SeaLevel, context.MinY, random, 10);
        }
        while (builder.IsEmpty || start.PortalRoom is null);
    }
}

/// <summary>
/// Vanilla's <c>StrongholdPieces</c>: the stronghold's pieces and how they're picked. Piece type names match vanilla's.
/// </summary>
public static class StrongholdPieces
{
    private const int MaxDepth = 50;

    private static readonly IBlock stoneBricks = BlocksRegistry.Get(Material.StoneBricks);
    private static readonly IBlock caveAir = BlocksRegistry.Get(Material.CaveAir);
    private static readonly IBlock smoothStoneSlab = BlocksRegistry.Get(Material.SmoothStoneSlab);
    private static readonly IBlock stoneBrickSlab = BlocksRegistry.Get(Material.StoneBrickSlab);
    private static readonly IBlock oakPlanks = BlocksRegistry.Get(Material.OakPlanks);
    private static readonly IBlock bookshelf = BlocksRegistry.Get(Material.Bookshelf);
    private static readonly IBlock cobblestone = BlocksRegistry.Get(Material.Cobblestone);
    private static readonly IBlock lava = BlocksRegistry.Get(Material.Lava);
    private static readonly IBlock wallTorch = BlocksRegistry.Get(Material.WallTorch);

    internal enum PieceKind
    {
        Straight,
        PrisonHall,
        LeftTurn,
        RightTurn,
        RoomCrossing,
        StraightStairsDown,
        StairsDown,
        FiveCrossing,
        ChestCorridor,
        Library,
        PortalRoom
    }

    /// <summary>
    /// Vanilla's piece weights, in order; libraries and the portal room also need some depth.
    /// </summary>
    private static PieceWeight[] CreateWeights() =>
    [
        new(PieceKind.Straight, 40, 0),
        new(PieceKind.PrisonHall, 5, 5),
        new(PieceKind.LeftTurn, 20, 0),
        new(PieceKind.RightTurn, 20, 0),
        new(PieceKind.RoomCrossing, 10, 6),
        new(PieceKind.StraightStairsDown, 5, 5),
        new(PieceKind.StairsDown, 5, 5),
        new(PieceKind.FiveCrossing, 5, 4),
        new(PieceKind.ChestCorridor, 5, 4),
        new(PieceKind.Library, 10, 2, minDepthExclusive: 4),
        new(PieceKind.PortalRoom, 20, 1, minDepthExclusive: 5)
    ];

    private static StrongholdPiece? CreatePiece(PieceKind kind, IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z,
        BlockFace direction, int depth) => kind switch
    {
        PieceKind.Straight => Straight.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.PrisonHall => PrisonHall.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.LeftTurn => LeftTurn.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.RightTurn => RightTurn.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.RoomCrossing => RoomCrossing.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.StraightStairsDown => StraightStairsDown.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.StairsDown => StairsDown.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.FiveCrossing => FiveCrossing.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.ChestCorridor => ChestCorridor.CreatePiece(pieces, random, x, y, z, direction, depth),
        PieceKind.Library => Library.CreatePiece(pieces, random, x, y, z, direction, depth),
        _ => PortalRoom.CreatePiece(pieces, x, y, z, direction, depth)
    };

    /// <summary>
    /// Vanilla <c>generatePieceFromSmallDoor</c>: a random piece by weight (up to 5 tries), else a filler corridor up to
    /// the piece in the way.
    /// </summary>
    private static StrongholdPiece? GeneratePieceFromSmallDoor(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int x,
        int y, int z, BlockFace direction, int depth)
    {
        if (!start.UpdatePieceWeight())
            return null;

        if (start.ImposedPiece is not null)
        {
            var imposed = start.ImposedPiece.Value;
            start.ImposedPiece = null;
            var piece = CreatePiece(imposed, pieces, random, x, y, z, direction, depth);
            if (piece is not null)
                return piece;
        }

        for (var attempt = 0; attempt < 5; attempt++)
        {
            var selection = random.NextInt(start.TotalWeight);

            // Like vanilla, a piece that can't fit falls through to the next one in the list.
            foreach (var weight in start.Weights)
            {
                selection -= weight.Weight;
                if (selection >= 0)
                    continue;

                if (!weight.CanPlace(depth) || weight == start.PreviousPiece)
                    break;

                var piece = CreatePiece(weight.Kind, pieces, random, x, y, z, direction, depth);
                if (piece is null)
                    continue;

                weight.PlaceCount++;
                start.PreviousPiece = weight;
                if (!weight.IsValid)
                    start.Weights.Remove(weight);

                return piece;
            }
        }

        var box = FillerCorridor.FindPieceBox(pieces, x, y, z, direction);
        return box is not null && box.Value.MinY > 1 ? new FillerCorridor(depth, box.Value, direction) : null;
    }

    private static StrongholdPiece? GenerateAndAddPiece(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z,
        BlockFace direction, int depth)
    {
        if (depth > MaxDepth)
            return null;

        if (Math.Abs(x - start.BoundingBox.MinX) > 112 || Math.Abs(z - start.BoundingBox.MinZ) > 112)
            return null;

        var piece = GeneratePieceFromSmallDoor(start, pieces, random, x, y, z, direction, depth + 1);
        if (piece is not null)
        {
            pieces.AddPiece(piece);
            start.PendingChildren.Add(piece);
        }

        return piece;
    }

    private static IBlock Connected(Material material, params string[] sides)
    {
        var block = BlocksRegistry.Get(material);
        foreach (var side in sides)
            block = block.WithProperty(side, true);

        return block;
    }

    internal sealed class PieceWeight(PieceKind kind, int weight, int maxPlaceCount, int minDepthExclusive = 0)
    {
        public PieceKind Kind { get; } = kind;

        public int Weight { get; } = weight;

        public int PlaceCount { get; set; }

        public bool IsValid => maxPlaceCount == 0 || this.PlaceCount < maxPlaceCount;

        public bool HasLimit => maxPlaceCount > 0;

        public bool CanPlace(int depth) => this.IsValid && depth > minDepthExclusive;
    }

    /// <summary>
    /// Vanilla's <c>SmoothStoneSelector</c>: stone bricks, partly cracked, mossy or infested, around cave air.
    /// </summary>
    /// <remarks>
    /// It holds the block it picked, so each placement makes its own: pieces are placed from several chunks at once.
    /// </remarks>
    private sealed class SmoothStoneSelector : StructurePiece.BlockSelector
    {
        private static readonly IBlock cracked = BlocksRegistry.Get(Material.CrackedStoneBricks);
        private static readonly IBlock mossy = BlocksRegistry.Get(Material.MossyStoneBricks);
        private static readonly IBlock infested = BlocksRegistry.Get(Material.InfestedStoneBricks);

        public override void Next(IRandomSource random, int x, int y, int z, bool isEdge)
        {
            if (!isEdge)
            {
                this.NextBlock = caveAir;
                return;
            }

            var selection = random.NextFloat();
            this.NextBlock = selection < 0.2f ? cracked : selection < 0.5f ? mossy : selection < 0.55f ? infested : stoneBricks;
        }
    }

    /// <summary>
    /// Vanilla's <c>StrongholdPiece.SmallDoorType</c>.
    /// </summary>
    public enum SmallDoorType
    {
        Opening,
        WoodDoor,
        Grates,
        IronDoor
    }

    /// <summary>
    /// Vanilla's <c>StrongholdPiece</c>: doors and the children pieces leading out of them.
    /// </summary>
    public abstract class StrongholdPiece : StructurePiece
    {
        protected StrongholdPiece(int genDepth, BlockBox boundingBox) : base(genDepth, boundingBox)
        {
        }

        /// <summary>
        /// The door at the piece's entrance.
        /// </summary>
        public SmallDoorType EntryDoor { get; protected set; } = SmallDoorType.Opening;

        protected static bool IsOkBox(BlockBox box) => box.MinY > 10;

        /// <summary>
        /// Vanilla <c>randomSmallDoor</c>: openings 2 times in 5, else a wooden door, grates or an iron door.
        /// </summary>
        protected static SmallDoorType RandomSmallDoor(IRandomSource random) => random.NextInt(5) switch
        {
            2 => SmallDoorType.WoodDoor,
            3 => SmallDoorType.Grates,
            4 => SmallDoorType.IronDoor,
            _ => SmallDoorType.Opening
        };

        protected void GenerateSmallDoor(IWorldGenLevel level, BlockBox box, SmallDoorType door, int x, int y, int z)
        {
            switch (door)
            {
                case SmallDoorType.Opening:
                    this.GenerateBox(level, box, x, y, z, x + 3 - 1, y + 3 - 1, z, caveAir, caveAir, false);
                    break;
                case SmallDoorType.WoodDoor:
                    this.PlaceDoorFrame(level, box, x, y, z);
                    this.PlaceBlock(level, BlocksRegistry.Get(Material.OakDoor), x + 1, y, z, box);
                    this.PlaceBlock(level, BlocksRegistry.Get(Material.OakDoor).WithProperty("half", "upper"), x + 1, y + 1, z, box);
                    break;
                case SmallDoorType.Grates:
                    this.PlaceBlock(level, caveAir, x + 1, y, z, box);
                    this.PlaceBlock(level, caveAir, x + 1, y + 1, z, box);
                    this.PlaceBlock(level, Connected(Material.IronBars, "west"), x, y, z, box);
                    this.PlaceBlock(level, Connected(Material.IronBars, "west"), x, y + 1, z, box);
                    this.PlaceBlock(level, Connected(Material.IronBars, "east", "west"), x, y + 2, z, box);
                    this.PlaceBlock(level, Connected(Material.IronBars, "east", "west"), x + 1, y + 2, z, box);
                    this.PlaceBlock(level, Connected(Material.IronBars, "east", "west"), x + 2, y + 2, z, box);
                    this.PlaceBlock(level, Connected(Material.IronBars, "east"), x + 2, y + 1, z, box);
                    this.PlaceBlock(level, Connected(Material.IronBars, "east"), x + 2, y, z, box);
                    break;
                case SmallDoorType.IronDoor:
                    this.PlaceDoorFrame(level, box, x, y, z);
                    this.PlaceBlock(level, BlocksRegistry.Get(Material.IronDoor), x + 1, y, z, box);
                    this.PlaceBlock(level, BlocksRegistry.Get(Material.IronDoor).WithProperty("half", "upper"), x + 1, y + 1, z, box);
                    this.PlaceBlock(level, BlocksRegistry.Get(Material.StoneButton).WithProperty("facing", "north"), x + 2, y + 1, z + 1, box);
                    this.PlaceBlock(level, BlocksRegistry.Get(Material.StoneButton).WithProperty("facing", "south"), x + 2, y + 1, z - 1, box);
                    break;
            }
        }

        private void PlaceDoorFrame(IWorldGenLevel level, BlockBox box, int x, int y, int z)
        {
            this.PlaceBlock(level, stoneBricks, x, y, z, box);
            this.PlaceBlock(level, stoneBricks, x, y + 1, z, box);
            this.PlaceBlock(level, stoneBricks, x, y + 2, z, box);
            this.PlaceBlock(level, stoneBricks, x + 1, y + 2, z, box);
            this.PlaceBlock(level, stoneBricks, x + 2, y + 2, z, box);
            this.PlaceBlock(level, stoneBricks, x + 2, y + 1, z, box);
            this.PlaceBlock(level, stoneBricks, x + 2, y, z, box);
        }

        /// <summary>
        /// Vanilla <c>generateSmallDoorChildForward</c>: a child through the far wall.
        /// </summary>
        protected StructurePiece? GenerateSmallDoorChildForward(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int xOffset,
            int yOffset)
        {
            var box = this.BoundingBox;
            return this.Orientation switch
            {
                BlockFace.North => GenerateAndAddPiece(start, pieces, random, box.MinX + xOffset, box.MinY + yOffset, box.MinZ - 1, BlockFace.North,
                    this.GenDepth),
                BlockFace.South => GenerateAndAddPiece(start, pieces, random, box.MinX + xOffset, box.MinY + yOffset, box.MaxZ + 1, BlockFace.South,
                    this.GenDepth),
                BlockFace.West => GenerateAndAddPiece(start, pieces, random, box.MinX - 1, box.MinY + yOffset, box.MinZ + xOffset, BlockFace.West,
                    this.GenDepth),
                BlockFace.East => GenerateAndAddPiece(start, pieces, random, box.MaxX + 1, box.MinY + yOffset, box.MinZ + xOffset, BlockFace.East,
                    this.GenDepth),
                _ => null
            };
        }

        /// <summary>
        /// Vanilla <c>generateSmallDoorChildLeft</c>: a child through the west wall (north for pieces along the X axis).
        /// </summary>
        protected StructurePiece? GenerateSmallDoorChildLeft(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int yOffset,
            int zOffset)
        {
            var box = this.BoundingBox;
            return this.Orientation switch
            {
                BlockFace.North or BlockFace.South => GenerateAndAddPiece(start, pieces, random, box.MinX - 1, box.MinY + yOffset, box.MinZ + zOffset,
                    BlockFace.West, this.GenDepth),
                BlockFace.West or BlockFace.East => GenerateAndAddPiece(start, pieces, random, box.MinX + zOffset, box.MinY + yOffset, box.MinZ - 1,
                    BlockFace.North, this.GenDepth),
                _ => null
            };
        }

        /// <summary>
        /// Vanilla <c>generateSmallDoorChildRight</c>: a child through the east wall (south for pieces along the X axis).
        /// </summary>
        protected StructurePiece? GenerateSmallDoorChildRight(StartPiece start, IStructurePieceAccessor pieces, IRandomSource random, int yOffset,
            int zOffset)
        {
            var box = this.BoundingBox;
            return this.Orientation switch
            {
                BlockFace.North or BlockFace.South => GenerateAndAddPiece(start, pieces, random, box.MaxX + 1, box.MinY + yOffset, box.MinZ + zOffset,
                    BlockFace.East, this.GenDepth),
                BlockFace.West or BlockFace.East => GenerateAndAddPiece(start, pieces, random, box.MinX + zOffset, box.MinY + yOffset, box.MaxZ + 1,
                    BlockFace.South, this.GenDepth),
                _ => null
            };
        }
    }

    /// <summary>
    /// Vanilla's <c>ChestCorridor</c>: a corridor with a chest on a slab shelf.
    /// </summary>
    public sealed class ChestCorridor : StrongholdPiece
    {
        // Like vanilla, set by whichever chunk places the chest.
        private bool hasPlacedChest;

        internal override void SaveState(NbtCompound tag)
        {
            base.SaveState(tag);

            tag.Add(new NbtTag<bool>("Chest", this.hasPlacedChest));
        }

        internal override void LoadState(NbtCompound tag)
        {
            base.LoadState(tag);

            this.hasPlacedChest = tag.TryGetBool("Chest", out var placed) && placed;
        }

        public ChestCorridor(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateSmallDoorChildForward((StartPiece)start, pieces, random, 1, 1);

        internal static ChestCorridor? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -1, 0, 5, 5, 7, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new ChestCorridor(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            var random = context.Random;
            this.GenerateBox(level, box, 0, 0, 0, 4, 4, 6, true, random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 1, 1, 0);
            this.GenerateSmallDoor(level, box, SmallDoorType.Opening, 1, 1, 6);
            this.GenerateBox(level, box, 3, 1, 2, 3, 1, 4, stoneBricks, stoneBricks, false);
            this.PlaceBlock(level, stoneBrickSlab, 3, 1, 1, box);
            this.PlaceBlock(level, stoneBrickSlab, 3, 1, 5, box);
            this.PlaceBlock(level, stoneBrickSlab, 3, 2, 2, box);
            this.PlaceBlock(level, stoneBrickSlab, 3, 2, 4, box);

            for (var z = 2; z <= 4; z++)
                this.PlaceBlock(level, stoneBrickSlab, 2, 1, z, box);

            if (!this.hasPlacedChest && box.IsInside(this.GetWorldPos(3, 2, 3)))
            {
                this.hasPlacedChest = true;
                this.CreateChest(level, box, random, 3, 2, 3, "minecraft:chests/stronghold_corridor");
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>FillerCorridor</c>: a short corridor joining a door to the piece in front of it.
    /// </summary>
    public sealed class FillerCorridor : StrongholdPiece
    {
        private readonly int steps;

        public FillerCorridor(int genDepth, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.steps = direction is BlockFace.North or BlockFace.South ? boundingBox.ZSpan : boundingBox.XSpan;
        }

        /// <summary>
        /// Vanilla <c>findPieceBox</c>: a 1 to 3 blocks long corridor up to a piece at the same height, or <c>null</c>.
        /// </summary>
        internal static BlockBox? FindPieceBox(IStructurePieceAccessor pieces, int x, int y, int z, BlockFace direction)
        {
            var box = BlockBox.Orient(x, y, z, -1, -1, 0, 5, 5, 4, direction);
            var collision = pieces.FindCollisionPiece(box);
            if (collision is null || collision.BoundingBox.MinY != box.MinY)
                return null;

            for (var depth = 2; depth >= 1; depth--)
            {
                box = BlockBox.Orient(x, y, z, -1, -1, 0, 5, 5, depth, direction);
                if (!collision.BoundingBox.Intersects(box))
                    return BlockBox.Orient(x, y, z, -1, -1, 0, 5, 5, depth + 1, direction);
            }

            return null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var level = context.Level;
            var box = context.Box;
            for (var i = 0; i < this.steps; i++)
            {
                for (var x = 0; x <= 4; x++)
                    this.PlaceBlock(level, stoneBricks, x, 0, i, box);

                for (var y = 1; y <= 3; y++)
                {
                    this.PlaceBlock(level, stoneBricks, 0, y, i, box);
                    this.PlaceBlock(level, caveAir, 1, y, i, box);
                    this.PlaceBlock(level, caveAir, 2, y, i, box);
                    this.PlaceBlock(level, caveAir, 3, y, i, box);
                    this.PlaceBlock(level, stoneBricks, 4, y, i, box);
                }

                for (var x = 0; x <= 4; x++)
                    this.PlaceBlock(level, stoneBricks, x, 4, i, box);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>FiveCrossing</c>: a tall junction with up to four side exits on two levels.
    /// </summary>
    public sealed class FiveCrossing : StrongholdPiece
    {
        private readonly bool leftLow;
        private readonly bool leftHigh;
        private readonly bool rightLow;
        private readonly bool rightHigh;

        public FiveCrossing(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
            this.leftLow = random.NextBoolean();
            this.leftHigh = random.NextBoolean();
            this.rightLow = random.NextBoolean();
            this.rightHigh = random.NextInt(3) > 0;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var strongholdStart = (StartPiece)start;
            var lowOffset = 3;
            var highOffset = 5;
            if (this.Orientation is BlockFace.West or BlockFace.North)
            {
                lowOffset = 8 - lowOffset;
                highOffset = 8 - highOffset;
            }

            // Like vanilla, the side offsets go in as the children's height offsets.
            this.GenerateSmallDoorChildForward(strongholdStart, pieces, random, 5, 1);
            if (this.leftLow)
                this.GenerateSmallDoorChildLeft(strongholdStart, pieces, random, lowOffset, 1);

            if (this.leftHigh)
                this.GenerateSmallDoorChildLeft(strongholdStart, pieces, random, highOffset, 7);

            if (this.rightLow)
                this.GenerateSmallDoorChildRight(strongholdStart, pieces, random, lowOffset, 1);

            if (this.rightHigh)
                this.GenerateSmallDoorChildRight(strongholdStart, pieces, random, highOffset, 7);
        }

        internal static FiveCrossing? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -4, -3, 0, 10, 9, 11, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new FiveCrossing(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            var random = context.Random;
            this.GenerateBox(level, box, 0, 0, 0, 9, 8, 10, true, random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 4, 3, 0);
            if (this.leftLow)
                this.GenerateBox(level, box, 0, 3, 1, 0, 5, 3, caveAir, caveAir, false);

            if (this.rightLow)
                this.GenerateBox(level, box, 9, 3, 1, 9, 5, 3, caveAir, caveAir, false);

            if (this.leftHigh)
                this.GenerateBox(level, box, 0, 5, 7, 0, 7, 9, caveAir, caveAir, false);

            if (this.rightHigh)
                this.GenerateBox(level, box, 9, 5, 7, 9, 7, 9, caveAir, caveAir, false);

            this.GenerateBox(level, box, 5, 1, 10, 7, 3, 10, caveAir, caveAir, false);
            this.GenerateBox(level, box, 1, 2, 1, 8, 2, 6, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 4, 1, 5, 4, 4, 9, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 8, 1, 5, 8, 4, 9, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 1, 4, 7, 3, 4, 9, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 1, 3, 5, 3, 3, 6, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 1, 3, 4, 3, 3, 4, smoothStoneSlab, smoothStoneSlab, false);
            this.GenerateBox(level, box, 1, 4, 6, 3, 4, 6, smoothStoneSlab, smoothStoneSlab, false);
            this.GenerateBox(level, box, 5, 1, 7, 7, 1, 8, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 5, 1, 9, 7, 1, 9, smoothStoneSlab, smoothStoneSlab, false);
            this.GenerateBox(level, box, 5, 2, 7, 7, 2, 7, smoothStoneSlab, smoothStoneSlab, false);
            this.GenerateBox(level, box, 4, 5, 7, 4, 5, 9, smoothStoneSlab, smoothStoneSlab, false);
            this.GenerateBox(level, box, 8, 5, 7, 8, 5, 9, smoothStoneSlab, smoothStoneSlab, false);
            var doubleSlab = smoothStoneSlab.WithProperty("type", "double");
            this.GenerateBox(level, box, 5, 5, 7, 7, 5, 9, doubleSlab, doubleSlab, false);
            this.PlaceBlock(level, wallTorch.WithProperty("facing", "south"), 6, 5, 6, box);
        }
    }

    /// <summary>
    /// Vanilla's <c>LeftTurn</c>.
    /// </summary>
    public sealed class LeftTurn : StrongholdPiece
    {
        public LeftTurn(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
        }

        // Pieces facing south or west are mirrored, so their left is the world's right.
        private bool TurnsRight => this.Orientation is not (BlockFace.North or BlockFace.East);

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            if (this.TurnsRight)
                this.GenerateSmallDoorChildRight((StartPiece)start, pieces, random, 1, 1);
            else
                this.GenerateSmallDoorChildLeft((StartPiece)start, pieces, random, 1, 1);
        }

        internal static LeftTurn? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -1, 0, 5, 5, 5, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new LeftTurn(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            this.GenerateBox(level, box, 0, 0, 0, 4, 4, 4, true, context.Random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 1, 1, 0);
            if (this.TurnsRight)
                this.GenerateBox(level, box, 4, 1, 1, 4, 3, 3, caveAir, caveAir, false);
            else
                this.GenerateBox(level, box, 0, 1, 1, 0, 3, 3, caveAir, caveAir, false);
        }
    }

    /// <summary>
    /// Vanilla's <c>RightTurn</c>.
    /// </summary>
    public sealed class RightTurn : StrongholdPiece
    {
        public RightTurn(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
        }

        // Pieces facing south or west are mirrored, so their right is the world's left.
        private bool TurnsLeft => this.Orientation is not (BlockFace.North or BlockFace.East);

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            if (this.TurnsLeft)
                this.GenerateSmallDoorChildLeft((StartPiece)start, pieces, random, 1, 1);
            else
                this.GenerateSmallDoorChildRight((StartPiece)start, pieces, random, 1, 1);
        }

        internal static RightTurn? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -1, 0, 5, 5, 5, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new RightTurn(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            this.GenerateBox(level, box, 0, 0, 0, 4, 4, 4, true, context.Random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 1, 1, 0);
            if (this.TurnsLeft)
                this.GenerateBox(level, box, 0, 1, 1, 0, 3, 3, caveAir, caveAir, false);
            else
                this.GenerateBox(level, box, 4, 1, 1, 4, 3, 3, caveAir, caveAir, false);
        }
    }

    /// <summary>
    /// Vanilla's <c>Library</c>: bookshelves, sometimes two floors high with a chandelier and a second chest.
    /// </summary>
    public sealed class Library : StrongholdPiece
    {
        private readonly bool isTall;

        public Library(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
            this.isTall = boundingBox.YSpan > 6;
        }

        internal static Library? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -4, -1, 0, 14, 11, 15, direction);
            if (!IsOkBox(box) || pieces.FindCollisionPiece(box) is not null)
            {
                box = BlockBox.Orient(x, y, z, -4, -1, 0, 14, 6, 15, direction);
                if (!IsOkBox(box) || pieces.FindCollisionPiece(box) is not null)
                    return null;
            }

            return new Library(genDepth, random, box, direction);
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            var random = context.Random;
            var height = this.isTall ? 11 : 6;
            this.GenerateBox(level, box, 0, 0, 0, 13, height - 1, 14, true, random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 4, 1, 0);
            var cobweb = BlocksRegistry.Get(Material.Cobweb);
            this.GenerateMaybeBox(level, box, random, 0.07f, 2, 1, 1, 11, 4, 13, cobweb, cobweb, false, false);

            for (var z = 1; z <= 13; z++)
            {
                // Plank pillars with torches every 4 blocks, bookshelves between.
                var isPillar = (z - 1) % 4 == 0;
                var shelf = isPillar ? oakPlanks : bookshelf;
                this.GenerateBox(level, box, 1, 1, z, 1, 4, z, shelf, shelf, false);
                this.GenerateBox(level, box, 12, 1, z, 12, 4, z, shelf, shelf, false);
                if (isPillar)
                {
                    this.PlaceBlock(level, wallTorch.WithProperty("facing", "east"), 2, 3, z, box);
                    this.PlaceBlock(level, wallTorch.WithProperty("facing", "west"), 11, 3, z, box);
                }

                if (this.isTall)
                {
                    this.GenerateBox(level, box, 1, 6, z, 1, 9, z, shelf, shelf, false);
                    this.GenerateBox(level, box, 12, 6, z, 12, 9, z, shelf, shelf, false);
                }
            }

            for (var z = 3; z < 12; z += 2)
            {
                this.GenerateBox(level, box, 3, 1, z, 4, 3, z, bookshelf, bookshelf, false);
                this.GenerateBox(level, box, 6, 1, z, 7, 3, z, bookshelf, bookshelf, false);
                this.GenerateBox(level, box, 9, 1, z, 10, 3, z, bookshelf, bookshelf, false);
            }

            if (this.isTall)
                this.PlaceUpperFloor(level, box);

            this.CreateChest(level, box, random, 3, 3, 5, "minecraft:chests/stronghold_library");
            if (this.isTall)
            {
                this.PlaceBlock(level, caveAir, 12, 9, 1, box);
                this.CreateChest(level, box, random, 12, 8, 1, "minecraft:chests/stronghold_library");
            }
        }

        private void PlaceUpperFloor(IWorldGenLevel level, BlockBox box)
        {
            this.GenerateBox(level, box, 1, 5, 1, 3, 5, 13, oakPlanks, oakPlanks, false);
            this.GenerateBox(level, box, 10, 5, 1, 12, 5, 13, oakPlanks, oakPlanks, false);
            this.GenerateBox(level, box, 4, 5, 1, 9, 5, 2, oakPlanks, oakPlanks, false);
            this.GenerateBox(level, box, 4, 5, 12, 9, 5, 13, oakPlanks, oakPlanks, false);
            this.PlaceBlock(level, oakPlanks, 9, 5, 11, box);
            this.PlaceBlock(level, oakPlanks, 8, 5, 11, box);
            this.PlaceBlock(level, oakPlanks, 9, 5, 10, box);

            var westEast = Connected(Material.OakFence, "west", "east");
            var northSouth = Connected(Material.OakFence, "north", "south");
            this.GenerateBox(level, box, 3, 6, 3, 3, 6, 11, northSouth, northSouth, false);
            this.GenerateBox(level, box, 10, 6, 3, 10, 6, 9, northSouth, northSouth, false);
            this.GenerateBox(level, box, 4, 6, 2, 9, 6, 2, westEast, westEast, false);
            this.GenerateBox(level, box, 4, 6, 12, 7, 6, 12, westEast, westEast, false);
            this.PlaceBlock(level, Connected(Material.OakFence, "north", "east"), 3, 6, 2, box);
            this.PlaceBlock(level, Connected(Material.OakFence, "south", "east"), 3, 6, 12, box);
            this.PlaceBlock(level, Connected(Material.OakFence, "north", "west"), 10, 6, 2, box);

            for (var i = 0; i <= 2; i++)
            {
                this.PlaceBlock(level, Connected(Material.OakFence, "south", "west"), 8 + i, 6, 12 - i, box);
                if (i != 2)
                    this.PlaceBlock(level, Connected(Material.OakFence, "north", "east"), 8 + i, 6, 11 - i, box);
            }

            var ladder = BlocksRegistry.Get(Material.Ladder).WithProperty("facing", "south");
            for (var y = 1; y <= 7; y++)
                this.PlaceBlock(level, ladder, 10, y, 13, box);

            // The chandelier.
            var east = Connected(Material.OakFence, "east");
            var west = Connected(Material.OakFence, "west");
            this.PlaceBlock(level, east, 6, 9, 7, box);
            this.PlaceBlock(level, west, 7, 9, 7, box);
            this.PlaceBlock(level, east, 6, 8, 7, box);
            this.PlaceBlock(level, west, 7, 8, 7, box);
            var allSides = Connected(Material.OakFence, "north", "south", "west", "east");
            this.PlaceBlock(level, allSides, 6, 7, 7, box);
            this.PlaceBlock(level, allSides, 7, 7, 7, box);
            this.PlaceBlock(level, east, 5, 7, 7, box);
            this.PlaceBlock(level, west, 8, 7, 7, box);
            this.PlaceBlock(level, Connected(Material.OakFence, "east", "north"), 6, 7, 6, box);
            this.PlaceBlock(level, Connected(Material.OakFence, "east", "south"), 6, 7, 8, box);
            this.PlaceBlock(level, Connected(Material.OakFence, "west", "north"), 7, 7, 6, box);
            this.PlaceBlock(level, Connected(Material.OakFence, "west", "south"), 7, 7, 8, box);

            var torch = BlocksRegistry.Get(Material.Torch);
            this.PlaceBlock(level, torch, 5, 8, 7, box);
            this.PlaceBlock(level, torch, 8, 8, 7, box);
            this.PlaceBlock(level, torch, 6, 8, 6, box);
            this.PlaceBlock(level, torch, 6, 8, 8, box);
            this.PlaceBlock(level, torch, 7, 8, 6, box);
            this.PlaceBlock(level, torch, 7, 8, 8, box);
        }
    }

    /// <summary>
    /// Vanilla's <c>PortalRoom</c>: the end portal over a lava pool, with a silverfish spawner on the stairs.
    /// </summary>
    public sealed class PortalRoom : StrongholdPiece
    {
        // Like vanilla, set by whichever chunk places the spawner.
        private bool hasPlacedSpawner;

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

        public PortalRoom(int genDepth, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            ((StartPiece)start).PortalRoom = this;

        internal static PortalRoom? CreatePiece(IStructurePieceAccessor pieces, int x, int y, int z, BlockFace direction, int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -4, -1, 0, 11, 8, 16, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new PortalRoom(genDepth, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            var random = context.Random;
            this.GenerateBox(level, box, 0, 0, 0, 10, 7, 15, false, random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, SmallDoorType.Grates, 4, 1, 0);
            this.GenerateBox(level, box, 1, 6, 1, 1, 6, 14, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 9, 6, 1, 9, 6, 14, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 2, 6, 1, 8, 6, 2, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 2, 6, 14, 8, 6, 14, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 1, 1, 1, 2, 1, 4, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 8, 1, 1, 9, 1, 4, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 1, 1, 1, 1, 1, 3, lava, lava, false);
            this.GenerateBox(level, box, 9, 1, 1, 9, 1, 3, lava, lava, false);
            this.GenerateBox(level, box, 3, 1, 8, 7, 1, 12, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 4, 1, 9, 6, 1, 11, lava, lava, false);

            var northSouthBars = Connected(Material.IronBars, "north", "south");
            var westEastBars = Connected(Material.IronBars, "west", "east");
            for (var z = 3; z < 14; z += 2)
            {
                this.GenerateBox(level, box, 0, 3, z, 0, 4, z, northSouthBars, northSouthBars, false);
                this.GenerateBox(level, box, 10, 3, z, 10, 4, z, northSouthBars, northSouthBars, false);
            }

            for (var x = 2; x < 9; x += 2)
                this.GenerateBox(level, box, x, 3, 15, x, 4, 15, westEastBars, westEastBars, false);

            var stairs = BlocksRegistry.Get(Material.StoneBrickStairs).WithProperty("facing", "north");
            this.GenerateBox(level, box, 4, 1, 5, 6, 1, 7, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 4, 2, 6, 6, 2, 7, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 4, 3, 7, 6, 3, 7, false, random, smoothStoneSelector);

            for (var x = 4; x <= 6; x++)
            {
                this.PlaceBlock(level, stairs, x, 1, 4, box);
                this.PlaceBlock(level, stairs, x, 2, 5, box);
                this.PlaceBlock(level, stairs, x, 3, 6, box);
            }

            var eyes = new bool[12];
            var allEyes = true;
            for (var i = 0; i < eyes.Length; i++)
            {
                eyes[i] = random.NextFloat() > 0.9f;
                allEyes &= eyes[i];
            }

            var frame = BlocksRegistry.Get(Material.EndPortalFrame);
            IBlock Frame(string facing, int eye) => frame.WithProperty("facing", facing).WithProperty("eye", eyes[eye]);
            this.PlaceBlock(level, Frame("north", 0), 4, 3, 8, box);
            this.PlaceBlock(level, Frame("north", 1), 5, 3, 8, box);
            this.PlaceBlock(level, Frame("north", 2), 6, 3, 8, box);
            this.PlaceBlock(level, Frame("south", 3), 4, 3, 12, box);
            this.PlaceBlock(level, Frame("south", 4), 5, 3, 12, box);
            this.PlaceBlock(level, Frame("south", 5), 6, 3, 12, box);
            this.PlaceBlock(level, Frame("east", 6), 3, 3, 9, box);
            this.PlaceBlock(level, Frame("east", 7), 3, 3, 10, box);
            this.PlaceBlock(level, Frame("east", 8), 3, 3, 11, box);
            this.PlaceBlock(level, Frame("west", 9), 7, 3, 9, box);
            this.PlaceBlock(level, Frame("west", 10), 7, 3, 10, box);
            this.PlaceBlock(level, Frame("west", 11), 7, 3, 11, box);
            if (allEyes)
            {
                var portal = BlocksRegistry.Get(Material.EndPortal);
                for (var z = 9; z <= 11; z++)
                {
                    for (var x = 4; x <= 6; x++)
                        this.PlaceBlock(level, portal, x, 3, z, box);
                }
            }

            if (this.hasPlacedSpawner)
                return;

            var position = this.GetWorldPos(5, 3, 6);
            if (box.IsInside(position))
            {
                this.hasPlacedSpawner = true;
                level.SetBlock(position, BlocksRegistry.Get(Material.Spawner));
                FeatureHelpers.SetSpawnerEntity(level, position, () => "minecraft:silverfish");
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>PrisonHall</c>: two cells behind iron bars and doors.
    /// </summary>
    public sealed class PrisonHall : StrongholdPiece
    {
        public PrisonHall(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateSmallDoorChildForward((StartPiece)start, pieces, random, 1, 1);

        internal static PrisonHall? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -1, 0, 9, 5, 11, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new PrisonHall(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            var random = context.Random;
            this.GenerateBox(level, box, 0, 0, 0, 8, 4, 10, true, random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 1, 1, 0);
            this.GenerateBox(level, box, 1, 1, 10, 3, 3, 10, caveAir, caveAir, false);
            this.GenerateBox(level, box, 4, 1, 1, 4, 3, 1, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 4, 1, 3, 4, 3, 3, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 4, 1, 7, 4, 3, 7, false, random, smoothStoneSelector);
            this.GenerateBox(level, box, 4, 1, 9, 4, 3, 9, false, random, smoothStoneSelector);

            var northSouth = Connected(Material.IronBars, "north", "south");
            var westEast = Connected(Material.IronBars, "west", "east");
            for (var y = 1; y <= 3; y++)
            {
                this.PlaceBlock(level, northSouth, 4, y, 4, box);
                this.PlaceBlock(level, Connected(Material.IronBars, "north", "south", "east"), 4, y, 5, box);
                this.PlaceBlock(level, northSouth, 4, y, 6, box);
                this.PlaceBlock(level, westEast, 5, y, 5, box);
                this.PlaceBlock(level, westEast, 6, y, 5, box);
                this.PlaceBlock(level, westEast, 7, y, 5, box);
            }

            this.PlaceBlock(level, northSouth, 4, 3, 2, box);
            this.PlaceBlock(level, northSouth, 4, 3, 8, box);
            var doorBottom = BlocksRegistry.Get(Material.IronDoor).WithProperty("facing", "west");
            var doorTop = doorBottom.WithProperty("half", "upper");
            this.PlaceBlock(level, doorBottom, 4, 1, 2, box);
            this.PlaceBlock(level, doorTop, 4, 2, 2, box);
            this.PlaceBlock(level, doorBottom, 4, 1, 8, box);
            this.PlaceBlock(level, doorTop, 4, 2, 8, box);
        }
    }

    /// <summary>
    /// Vanilla's <c>RoomCrossing</c>: a junction room, empty or with a pillar, a fountain or a storeroom with a chest.
    /// </summary>
    public sealed class RoomCrossing : StrongholdPiece
    {
        private readonly int type;

        public RoomCrossing(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
            this.type = random.NextInt(5);
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var strongholdStart = (StartPiece)start;
            this.GenerateSmallDoorChildForward(strongholdStart, pieces, random, 4, 1);
            this.GenerateSmallDoorChildLeft(strongholdStart, pieces, random, 1, 4);
            this.GenerateSmallDoorChildRight(strongholdStart, pieces, random, 1, 4);
        }

        internal static RoomCrossing? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -4, -1, 0, 11, 7, 11, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new RoomCrossing(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            var random = context.Random;
            this.GenerateBox(level, box, 0, 0, 0, 10, 6, 10, true, random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 4, 1, 0);
            this.GenerateBox(level, box, 4, 1, 10, 6, 3, 10, caveAir, caveAir, false);
            this.GenerateBox(level, box, 0, 1, 4, 0, 3, 6, caveAir, caveAir, false);
            this.GenerateBox(level, box, 10, 1, 4, 10, 3, 6, caveAir, caveAir, false);

            switch (this.type)
            {
                case 0:
                    this.PlaceBlock(level, stoneBricks, 5, 1, 5, box);
                    this.PlaceBlock(level, stoneBricks, 5, 2, 5, box);
                    this.PlaceBlock(level, stoneBricks, 5, 3, 5, box);
                    this.PlaceBlock(level, wallTorch.WithProperty("facing", "west"), 4, 3, 5, box);
                    this.PlaceBlock(level, wallTorch.WithProperty("facing", "east"), 6, 3, 5, box);
                    this.PlaceBlock(level, wallTorch.WithProperty("facing", "south"), 5, 3, 4, box);
                    this.PlaceBlock(level, wallTorch.WithProperty("facing", "north"), 5, 3, 6, box);
                    this.PlaceBlock(level, smoothStoneSlab, 4, 1, 4, box);
                    this.PlaceBlock(level, smoothStoneSlab, 4, 1, 5, box);
                    this.PlaceBlock(level, smoothStoneSlab, 4, 1, 6, box);
                    this.PlaceBlock(level, smoothStoneSlab, 6, 1, 4, box);
                    this.PlaceBlock(level, smoothStoneSlab, 6, 1, 5, box);
                    this.PlaceBlock(level, smoothStoneSlab, 6, 1, 6, box);
                    this.PlaceBlock(level, smoothStoneSlab, 5, 1, 4, box);
                    this.PlaceBlock(level, smoothStoneSlab, 5, 1, 6, box);
                    break;
                case 1:
                    for (var i = 0; i < 5; i++)
                    {
                        this.PlaceBlock(level, stoneBricks, 3, 1, 3 + i, box);
                        this.PlaceBlock(level, stoneBricks, 7, 1, 3 + i, box);
                        this.PlaceBlock(level, stoneBricks, 3 + i, 1, 3, box);
                        this.PlaceBlock(level, stoneBricks, 3 + i, 1, 7, box);
                    }

                    this.PlaceBlock(level, stoneBricks, 5, 1, 5, box);
                    this.PlaceBlock(level, stoneBricks, 5, 2, 5, box);
                    this.PlaceBlock(level, stoneBricks, 5, 3, 5, box);
                    this.PlaceBlock(level, BlocksRegistry.Get(Material.Water), 5, 4, 5, box);
                    break;
                case 2:
                    this.PlaceStoreroom(level, box, random);
                    break;
            }
        }

        private void PlaceStoreroom(IWorldGenLevel level, BlockBox box, IRandomSource random)
        {
            for (var z = 1; z <= 9; z++)
            {
                this.PlaceBlock(level, cobblestone, 1, 3, z, box);
                this.PlaceBlock(level, cobblestone, 9, 3, z, box);
            }

            for (var x = 1; x <= 9; x++)
            {
                this.PlaceBlock(level, cobblestone, x, 3, 1, box);
                this.PlaceBlock(level, cobblestone, x, 3, 9, box);
            }

            this.PlaceBlock(level, cobblestone, 5, 1, 4, box);
            this.PlaceBlock(level, cobblestone, 5, 1, 6, box);
            this.PlaceBlock(level, cobblestone, 5, 3, 4, box);
            this.PlaceBlock(level, cobblestone, 5, 3, 6, box);
            this.PlaceBlock(level, cobblestone, 4, 1, 5, box);
            this.PlaceBlock(level, cobblestone, 6, 1, 5, box);
            this.PlaceBlock(level, cobblestone, 4, 3, 5, box);
            this.PlaceBlock(level, cobblestone, 6, 3, 5, box);

            for (var y = 1; y <= 3; y++)
            {
                this.PlaceBlock(level, cobblestone, 4, y, 4, box);
                this.PlaceBlock(level, cobblestone, 6, y, 4, box);
                this.PlaceBlock(level, cobblestone, 4, y, 6, box);
                this.PlaceBlock(level, cobblestone, 6, y, 6, box);
            }

            this.PlaceBlock(level, wallTorch, 5, 3, 5, box);

            for (var z = 2; z <= 8; z++)
            {
                this.PlaceBlock(level, oakPlanks, 2, 3, z, box);
                this.PlaceBlock(level, oakPlanks, 3, 3, z, box);
                if (z <= 3 || z >= 7)
                {
                    this.PlaceBlock(level, oakPlanks, 4, 3, z, box);
                    this.PlaceBlock(level, oakPlanks, 5, 3, z, box);
                    this.PlaceBlock(level, oakPlanks, 6, 3, z, box);
                }

                this.PlaceBlock(level, oakPlanks, 7, 3, z, box);
                this.PlaceBlock(level, oakPlanks, 8, 3, z, box);
            }

            var ladder = BlocksRegistry.Get(Material.Ladder).WithProperty("facing", "west");
            this.PlaceBlock(level, ladder, 9, 1, 3, box);
            this.PlaceBlock(level, ladder, 9, 2, 3, box);
            this.PlaceBlock(level, ladder, 9, 3, 3, box);
            this.CreateChest(level, box, random, 3, 4, 8, "minecraft:chests/stronghold_crossing");
        }
    }

    /// <summary>
    /// Vanilla's <c>StairsDown</c>: a spiral staircase; the start piece is one too.
    /// </summary>
    public class StairsDown : StrongholdPiece
    {
        // The start piece always leads into a five-way crossing.
        private readonly bool isSource;

        protected StairsDown(int genDepth, int x, int z, BlockFace direction)
            : base(genDepth, MakeBoundingBox(x, 64, z, direction, 5, 11, 5))
        {
            this.isSource = true;
            this.Orientation = direction;
            this.EntryDoor = SmallDoorType.Opening;
        }

        public StairsDown(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var strongholdStart = (StartPiece)start;
            if (this.isSource)
                strongholdStart.ImposedPiece = PieceKind.FiveCrossing;

            this.GenerateSmallDoorChildForward(strongholdStart, pieces, random, 1, 1);
        }

        internal static StairsDown? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -7, 0, 5, 11, 5, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new StairsDown(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            this.GenerateBox(level, box, 0, 0, 0, 4, 10, 4, true, context.Random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 1, 7, 0);
            this.GenerateSmallDoor(level, box, SmallDoorType.Opening, 1, 1, 4);
            this.PlaceBlock(level, stoneBricks, 2, 6, 1, box);
            this.PlaceBlock(level, stoneBricks, 1, 5, 1, box);
            this.PlaceBlock(level, smoothStoneSlab, 1, 6, 1, box);
            this.PlaceBlock(level, stoneBricks, 1, 5, 2, box);
            this.PlaceBlock(level, stoneBricks, 1, 4, 3, box);
            this.PlaceBlock(level, smoothStoneSlab, 1, 5, 3, box);
            this.PlaceBlock(level, stoneBricks, 2, 4, 3, box);
            this.PlaceBlock(level, stoneBricks, 3, 3, 3, box);
            this.PlaceBlock(level, smoothStoneSlab, 3, 4, 3, box);
            this.PlaceBlock(level, stoneBricks, 3, 3, 2, box);
            this.PlaceBlock(level, stoneBricks, 3, 2, 1, box);
            this.PlaceBlock(level, smoothStoneSlab, 3, 3, 1, box);
            this.PlaceBlock(level, stoneBricks, 2, 2, 1, box);
            this.PlaceBlock(level, stoneBricks, 1, 1, 1, box);
            this.PlaceBlock(level, smoothStoneSlab, 1, 2, 1, box);
            this.PlaceBlock(level, stoneBricks, 1, 1, 2, box);
            this.PlaceBlock(level, smoothStoneSlab, 1, 1, 3, box);
        }
    }

    /// <summary>
    /// Vanilla's <c>StartPiece</c>: the staircase the stronghold grows from, holding the state of the pieces being picked.
    /// </summary>
    public sealed class StartPiece : StairsDown
    {
        public StartPiece(IRandomSource random, int x, int z) : base(0, x, z, RandomHorizontalDirection(random))
        {
        }

        /// <summary>
        /// The portal room, once placed.
        /// </summary>
        public PortalRoom? PortalRoom { get; internal set; }

        /// <summary>
        /// Pieces whose children are still to be added, picked at random.
        /// </summary>
        public List<StructurePiece> PendingChildren { get; } = [];

        internal List<PieceWeight> Weights { get; } = [.. CreateWeights()];

        internal PieceWeight? PreviousPiece { get; set; }

        internal PieceKind? ImposedPiece { get; set; }

        internal int TotalWeight { get; private set; }

        /// <summary>
        /// Vanilla <c>updatePieceWeight</c>: sums the weights left, and whether any limited piece can still be placed.
        /// </summary>
        internal bool UpdatePieceWeight()
        {
            var hasAnyPieces = false;
            this.TotalWeight = 0;
            foreach (var weight in this.Weights)
            {
                if (weight.HasLimit && weight.IsValid)
                    hasAnyPieces = true;

                this.TotalWeight += weight.Weight;
            }

            return hasAnyPieces;
        }
    }

    /// <summary>
    /// Vanilla's <c>Straight</c>: a corridor, sometimes with side exits.
    /// </summary>
    public sealed class Straight : StrongholdPiece
    {
        private readonly bool leftChild;
        private readonly bool rightChild;

        public Straight(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
            this.leftChild = random.NextInt(2) == 0;
            this.rightChild = random.NextInt(2) == 0;
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
        {
            var strongholdStart = (StartPiece)start;
            this.GenerateSmallDoorChildForward(strongholdStart, pieces, random, 1, 1);
            if (this.leftChild)
                this.GenerateSmallDoorChildLeft(strongholdStart, pieces, random, 1, 2);

            if (this.rightChild)
                this.GenerateSmallDoorChildRight(strongholdStart, pieces, random, 1, 2);
        }

        internal static Straight? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction,
            int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -1, 0, 5, 5, 7, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new Straight(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            var random = context.Random;
            this.GenerateBox(level, box, 0, 0, 0, 4, 4, 6, true, random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 1, 1, 0);
            this.GenerateSmallDoor(level, box, SmallDoorType.Opening, 1, 1, 6);
            var eastTorch = wallTorch.WithProperty("facing", "east");
            var westTorch = wallTorch.WithProperty("facing", "west");
            this.MaybeGenerateBlock(level, box, random, 0.1f, 1, 2, 1, eastTorch);
            this.MaybeGenerateBlock(level, box, random, 0.1f, 3, 2, 1, westTorch);
            this.MaybeGenerateBlock(level, box, random, 0.1f, 1, 2, 5, eastTorch);
            this.MaybeGenerateBlock(level, box, random, 0.1f, 3, 2, 5, westTorch);
            if (this.leftChild)
                this.GenerateBox(level, box, 0, 1, 2, 0, 3, 4, caveAir, caveAir, false);

            if (this.rightChild)
                this.GenerateBox(level, box, 4, 1, 2, 4, 3, 4, caveAir, caveAir, false);
        }
    }

    /// <summary>
    /// Vanilla's <c>StraightStairsDown</c>: a straight flight of stairs down.
    /// </summary>
    public sealed class StraightStairsDown : StrongholdPiece
    {
        public StraightStairsDown(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction) : base(genDepth, boundingBox)
        {
            this.Orientation = direction;
            this.EntryDoor = RandomSmallDoor(random);
        }

        public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random) =>
            this.GenerateSmallDoorChildForward((StartPiece)start, pieces, random, 1, 1);

        internal static StraightStairsDown? CreatePiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z,
            BlockFace direction, int genDepth)
        {
            var box = BlockBox.Orient(x, y, z, -1, -7, 0, 5, 11, 8, direction);
            return IsOkBox(box) && pieces.FindCollisionPiece(box) is null ? new StraightStairsDown(genDepth, random, box, direction) : null;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var smoothStoneSelector = new SmoothStoneSelector();
            var level = context.Level;
            var box = context.Box;
            this.GenerateBox(level, box, 0, 0, 0, 4, 10, 7, true, context.Random, smoothStoneSelector);
            this.GenerateSmallDoor(level, box, this.EntryDoor, 1, 7, 0);
            this.GenerateSmallDoor(level, box, SmallDoorType.Opening, 1, 1, 7);
            var stairs = BlocksRegistry.Get(Material.CobblestoneStairs).WithProperty("facing", "south");

            for (var i = 0; i < 6; i++)
            {
                this.PlaceBlock(level, stairs, 1, 6 - i, 1 + i, box);
                this.PlaceBlock(level, stairs, 2, 6 - i, 1 + i, box);
                this.PlaceBlock(level, stairs, 3, 6 - i, 1 + i, box);
                if (i < 5)
                {
                    this.PlaceBlock(level, stoneBricks, 1, 5 - i, 1 + i, box);
                    this.PlaceBlock(level, stoneBricks, 2, 5 - i, 1 + i, box);
                    this.PlaceBlock(level, stoneBricks, 3, 5 - i, 1 + i, box);
                }
            }
        }
    }
}
