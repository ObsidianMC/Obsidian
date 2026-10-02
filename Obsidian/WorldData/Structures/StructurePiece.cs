using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// A part of a structure (a room, a corridor, a template...) with the block placement helpers vanilla's
/// <c>StructurePiece</c> offers.
/// </summary>
/// <remarks>
/// Pieces with an orientation use local coordinates: X across, Z along the orientation, Y up from the box bottom. They're
/// mirrored and rotated into the world like vanilla, and so are the blocks they place.
/// </remarks>
public abstract class StructurePiece
{
    // Blocks vanilla re-checks against their neighbors once the chunk is complete, since pieces place them without their
    // connections or support.
    private static readonly BlockSet shapeCheckBlocks = new("minecraft:nether_brick_fence", "minecraft:torch", "minecraft:wall_torch",
        "minecraft:oak_fence", "minecraft:spruce_fence", "minecraft:dark_oak_fence", "minecraft:pale_oak_fence", "minecraft:acacia_fence",
        "minecraft:birch_fence", "minecraft:jungle_fence", "minecraft:ladder", "minecraft:iron_bars");

    protected StructurePiece(int genDepth, BlockBox boundingBox)
    {
        this.GenDepth = genDepth;
        this.BoundingBox = boundingBox;
    }

    public BlockBox BoundingBox { get; protected set; }

    /// <summary>
    /// How many pieces away from the start piece this one was added.
    /// </summary>
    public int GenDepth { get; set; }

    /// <summary>
    /// The horizontal direction the piece faces, or <c>null</c> for pieces placed in world coordinates.
    /// </summary>
    public BlockFace? Orientation
    {
        get;
        set
        {
            field = value;

            // Vanilla setOrientation: local coordinates are built facing north, so the other directions mirror and rotate.
            (this.Mirror, this.Rotation) = value switch
            {
                BlockFace.South => (StructureMirror.LeftRight, StructureRotation.None),
                BlockFace.West => (StructureMirror.LeftRight, StructureRotation.Clockwise90),
                BlockFace.East => (StructureMirror.None, StructureRotation.Clockwise90),
                _ => (StructureMirror.None, StructureRotation.None)
            };
        }
    }

    public StructureMirror Mirror { get; private set; }

    public StructureRotation Rotation { get; private set; }

    /// <summary>
    /// Adds the pieces this one connects to while the structure is built.
    /// </summary>
    public virtual void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
    {
    }

    /// <summary>
    /// Places the part of the piece inside <see cref="StructurePieceContext.Box"/>.
    /// </summary>
    public abstract void PostProcess(StructurePieceContext context);

    public virtual void Move(int x, int y, int z) => this.BoundingBox = this.BoundingBox.Move(x, y, z);

    /// <summary>
    /// Vanilla <c>isCloseToChunk</c>: whether the box reaches within <paramref name="distance"/> blocks of the chunk.
    /// </summary>
    public bool IsCloseToChunk(int chunkX, int chunkZ, int distance)
    {
        var minX = chunkX << 4;
        var minZ = chunkZ << 4;
        return this.BoundingBox.Intersects(minX - distance, minZ - distance, minX + 15 + distance, minZ + 15 + distance);
    }

    /// <summary>
    /// The first piece whose box intersects <paramref name="box"/>, or <c>null</c>.
    /// </summary>
    public static StructurePiece? FindCollisionPiece(IEnumerable<StructurePiece> pieces, BlockBox box) =>
        pieces.FirstOrDefault(piece => piece.BoundingBox.Intersects(box));

    /// <summary>
    /// Vanilla <c>makeBoundingBox</c>: a box at (<paramref name="x"/>, <paramref name="y"/>, <paramref name="z"/>) whose width
    /// runs across <paramref name="direction"/> and depth along it.
    /// </summary>
    protected static BlockBox MakeBoundingBox(int x, int y, int z, BlockFace direction, int width, int height, int depth) =>
        direction is BlockFace.North or BlockFace.South
            ? BlockBox.Create(x, y, z, x + width - 1, y + height - 1, z + depth - 1)
            : BlockBox.Create(x, y, z, x + depth - 1, y + height - 1, z + width - 1);

    /// <summary>
    /// Vanilla <c>getRandomHorizontalDirection</c>: one <c>nextInt(4)</c> over north, east, south, west.
    /// </summary>
    protected static BlockFace RandomHorizontalDirection(IRandomSource random) => FeatureHelpers.RandomHorizontal(random);

    protected Vector GetWorldPos(int x, int y, int z) => new(this.GetWorldX(x, z), this.GetWorldY(y), this.GetWorldZ(x, z));

    protected int GetWorldX(int x, int z) => this.Orientation switch
    {
        BlockFace.North or BlockFace.South => this.BoundingBox.MinX + x,
        BlockFace.West => this.BoundingBox.MaxX - z,
        BlockFace.East => this.BoundingBox.MinX + z,
        _ => x
    };

    protected int GetWorldY(int y) => this.Orientation is null ? y : y + this.BoundingBox.MinY;

    protected int GetWorldZ(int x, int z) => this.Orientation switch
    {
        BlockFace.North => this.BoundingBox.MaxZ - z,
        BlockFace.South => this.BoundingBox.MinZ + z,
        BlockFace.West or BlockFace.East => this.BoundingBox.MinZ + x,
        _ => z
    };

    /// <summary>
    /// Vanilla <c>placeBlock</c>: places <paramref name="block"/> (mirrored and rotated with the piece) at local coordinates
    /// inside <paramref name="box"/>.
    /// </summary>
    protected void PlaceBlock(IWorldGenLevel level, IBlock block, int x, int y, int z, BlockBox box)
    {
        var position = this.GetWorldPos(x, y, z);
        if (!box.IsInside(position) || !this.CanBeReplaced(level, x, y, z, box))
            return;

        block = block.Mirror(this.Mirror).Rotate(this.Rotation);
        level.SetBlock(position, block);

        if (block.HasFluid())
            level.ScheduleFluidTick(position);

        if (shapeCheckBlocks.Contains(block))
            level.MarkForPostProcessing(position);
    }

    protected virtual bool CanBeReplaced(IWorldGenLevel level, int x, int y, int z, BlockBox box) => true;

    /// <summary>
    /// The block at local coordinates, or air outside <paramref name="box"/>.
    /// </summary>
    protected IBlock GetBlock(IWorldGenLevel level, int x, int y, int z, BlockBox box)
    {
        var position = this.GetWorldPos(x, y, z);
        return box.IsInside(position) ? level.GetBlock(position) : BlocksRegistry.Air;
    }

    /// <summary>
    /// Vanilla <c>isInterior</c>: whether the position above the local coordinates is below the ocean floor.
    /// </summary>
    protected bool IsInterior(IWorldGenLevel level, int x, int y, int z, BlockBox box)
    {
        var position = this.GetWorldPos(x, y + 1, z);
        return box.IsInside(position) && position.Y < level.GetHeight(HeightmapType.OceanFloorWG, position.X, position.Z);
    }

    protected void GenerateAirBox(IWorldGenLevel level, BlockBox box, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
    {
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                    this.PlaceBlock(level, BlocksRegistry.Air, x, y, z, box);
            }
        }
    }

    /// <summary>
    /// Vanilla <c>generateBox</c>: fills a local box, with <paramref name="edge"/> on its faces and <paramref name="inside"/>
    /// elsewhere.
    /// </summary>
    /// <param name="existingOnly">Only replaces blocks that aren't air.</param>
    protected void GenerateBox(IWorldGenLevel level, BlockBox box, int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
        IBlock edge, IBlock inside, bool existingOnly)
    {
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (existingOnly && this.GetBlock(level, x, y, z, box).IsAir)
                        continue;

                    var isEdge = y == minY || y == maxY || x == minX || x == maxX || z == minZ || z == maxZ;
                    this.PlaceBlock(level, isEdge ? edge : inside, x, y, z, box);
                }
            }
        }
    }

    protected void GenerateBox(IWorldGenLevel level, BlockBox box, BlockBox area, IBlock edge, IBlock inside, bool existingOnly) =>
        this.GenerateBox(level, box, area.MinX, area.MinY, area.MinZ, area.MaxX, area.MaxY, area.MaxZ, edge, inside, existingOnly);

    /// <summary>
    /// Vanilla <c>generateBox</c> with a block selector picking each block.
    /// </summary>
    protected void GenerateBox(IWorldGenLevel level, BlockBox box, int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
        bool existingOnly, IRandomSource random, BlockSelector selector)
    {
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (existingOnly && this.GetBlock(level, x, y, z, box).IsAir)
                        continue;

                    selector.Next(random, x, y, z, y == minY || y == maxY || x == minX || x == maxX || z == minZ || z == maxZ);
                    this.PlaceBlock(level, selector.NextBlock, x, y, z, box);
                }
            }
        }
    }

    protected void GenerateBox(IWorldGenLevel level, BlockBox box, BlockBox area, bool existingOnly, IRandomSource random, BlockSelector selector) =>
        this.GenerateBox(level, box, area.MinX, area.MinY, area.MinZ, area.MaxX, area.MaxY, area.MaxZ, existingOnly, random, selector);

    /// <summary>
    /// Vanilla <c>generateMaybeBox</c>: like <see cref="GenerateBox(IWorldGenLevel, BlockBox, int, int, int, int, int, int, IBlock, IBlock, bool)"/>,
    /// but each block is placed with <paramref name="probability"/>.
    /// </summary>
    /// <param name="interiorOnly">Only places blocks below the ocean floor.</param>
    protected void GenerateMaybeBox(IWorldGenLevel level, BlockBox box, IRandomSource random, float probability, int minX, int minY, int minZ,
        int maxX, int maxY, int maxZ, IBlock edge, IBlock inside, bool existingOnly, bool interiorOnly)
    {
        for (var y = minY; y <= maxY; y++)
        {
            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    if (random.NextFloat() > probability
                        || existingOnly && this.GetBlock(level, x, y, z, box).IsAir
                        || interiorOnly && !this.IsInterior(level, x, y, z, box))
                        continue;

                    var isEdge = y == minY || y == maxY || x == minX || x == maxX || z == minZ || z == maxZ;
                    this.PlaceBlock(level, isEdge ? edge : inside, x, y, z, box);
                }
            }
        }
    }

    protected void MaybeGenerateBlock(IWorldGenLevel level, BlockBox box, IRandomSource random, float probability, int x, int y, int z, IBlock block)
    {
        if (random.NextFloat() < probability)
            this.PlaceBlock(level, block, x, y, z, box);
    }

    /// <summary>
    /// Vanilla <c>generateUpperHalfSphere</c>: the upper half of an ellipsoid filling the local box.
    /// </summary>
    protected void GenerateUpperHalfSphere(IWorldGenLevel level, BlockBox box, int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
        IBlock block, bool existingOnly)
    {
        float width = maxX - minX + 1;
        float height = maxY - minY + 1;
        float depth = maxZ - minZ + 1;
        var centerX = minX + width / 2.0f;
        var centerZ = minZ + depth / 2.0f;

        for (var y = minY; y <= maxY; y++)
        {
            var relativeY = (y - minY) / height;

            for (var x = minX; x <= maxX; x++)
            {
                var relativeX = (x - centerX) / (width * 0.5f);

                for (var z = minZ; z <= maxZ; z++)
                {
                    var relativeZ = (z - centerZ) / (depth * 0.5f);
                    if (existingOnly && this.GetBlock(level, x, y, z, box).IsAir)
                        continue;

                    if (relativeX * relativeX + relativeY * relativeY + relativeZ * relativeZ <= 1.05f)
                        this.PlaceBlock(level, block, x, y, z, box);
                }
            }
        }
    }

    /// <summary>
    /// Vanilla <c>fillColumnDown</c>: fills down from the local coordinates through air, fluids and the like.
    /// </summary>
    protected void FillColumnDown(IWorldGenLevel level, IBlock block, int x, int y, int z, BlockBox box)
    {
        var position = this.GetWorldPos(x, y, z);
        if (!box.IsInside(position))
            return;

        while (IsReplaceableByStructures(level.GetBlock(position)) && position.Y > level.MinY + 1)
        {
            level.SetBlock(position, block);
            position += Vector.Down;
        }
    }

    protected static bool IsReplaceableByStructures(IBlock block) =>
        block.IsAir || block.IsLiquid || block.Material is Material.GlowLichen or Material.Seagrass or Material.TallSeagrass;

    /// <summary>
    /// Vanilla <c>createChest</c>: places a chest with <paramref name="lootTable"/> at local coordinates, facing away from a
    /// wall, unless there's already a chest.
    /// </summary>
    protected bool CreateChest(IWorldGenLevel level, BlockBox box, IRandomSource random, int x, int y, int z, string lootTable) =>
        this.CreateChest(level, box, random, this.GetWorldPos(x, y, z), lootTable, null);

    /// <summary>
    /// Vanilla <c>createChest</c> at world coordinates; <paramref name="chest"/> defaults to a chest facing away from a wall.
    /// </summary>
    protected bool CreateChest(IWorldGenLevel level, BlockBox box, IRandomSource random, Vector position, string lootTable, IBlock? chest)
    {
        if (!box.IsInside(position) || level.GetBlock(position).Material == Material.Chest)
            return false;

        level.SetBlock(position, chest ?? FeatureHelpers.Reorient(level, position, BlocksRegistry.Get(Material.Chest)));

        // Vanilla only seeds a ChestBlockEntity (chests and trapped chests).
        var container = level.GetBlockEntity(position) as DataBlockEntity;
        if (container?.Id is "minecraft:chest" or "minecraft:trapped_chest")
            SetLootTable(container, lootTable, random.NextLong());

        return true;
    }

    /// <summary>
    /// Vanilla <c>createDispenser</c>: places a dispenser facing <paramref name="facing"/> with <paramref name="lootTable"/>.
    /// </summary>
    protected bool CreateDispenser(IWorldGenLevel level, BlockBox box, IRandomSource random, int x, int y, int z, BlockFace facing, string lootTable)
    {
        var position = this.GetWorldPos(x, y, z);
        if (!box.IsInside(position) || level.GetBlock(position).Material == Material.Dispenser)
            return false;

        this.PlaceBlock(level, BlocksRegistry.Get(Material.Dispenser).WithProperty("facing", FeatureHelpers.FaceName(facing)), x, y, z, box);

        var container = level.GetBlockEntity(position) as DataBlockEntity;
        if (container?.Id == "minecraft:dispenser")
            SetLootTable(container, lootTable, random.NextLong());

        return true;
    }

    private static void SetLootTable(DataBlockEntity container, string lootTable, long seed)
    {
        container.Set("LootTable", lootTable);
        if (seed != 0L)
            container.Set("LootTableSeed", seed);
        else
            container.Data.Remove("LootTableSeed");
    }

    /// <summary>
    /// Picks blocks for <see cref="GenerateBox(IWorldGenLevel, BlockBox, int, int, int, int, int, int, bool, IRandomSource, BlockSelector)"/>,
    /// like vanilla's <c>StructurePiece.BlockSelector</c>.
    /// </summary>
    public abstract class BlockSelector
    {
        public IBlock NextBlock { get; protected set; } = BlocksRegistry.Air;

        /// <param name="isEdge">Whether the position is on a face of the box being filled.</param>
        public abstract void Next(IRandomSource random, int x, int y, int z, bool isEdge);
    }
}
