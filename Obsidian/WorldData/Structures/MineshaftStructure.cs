using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// Abandoned mineshafts: a room with corridors, crossings and stairs branching out, like vanilla's
/// <c>MineshaftStructure</c>.
/// </summary>
[StructureType("minecraft:mineshaft")]
public sealed class MineshaftStructure : Structure
{
    /// <summary>
    /// The wood the mineshaft is built from; badlands mineshafts are also raised towards the surface.
    /// </summary>
    public MineshaftType MineshaftType { get; init; }

    internal override StructureStub? FindGenerationPoint(StructureGenerationContext context)
    {
        context.Random.NextDouble();

        // The pieces are built before the biome check, since the start position depends on where they end up.
        var pieces = new StructurePiecesBuilder();
        var offset = this.GeneratePiecesAndAdjust(pieces, context);
        var position = new Vector((context.ChunkX << 4) + 8, MineshaftPieces.StartY + offset, context.ChunkZ << 4);
        return new StructureStub(position, builder =>
        {
            foreach (var piece in pieces.Pieces)
                builder.AddPiece(piece);
        });
    }

    private int GeneratePiecesAndAdjust(StructurePiecesBuilder builder, StructureGenerationContext context)
    {
        var random = context.Random;
        var room = new MineshaftRoom(0, random, (context.ChunkX << 4) + 2, (context.ChunkZ << 4) + 2, this.MineshaftType);
        builder.AddPiece(room);
        room.AddChildren(room, builder, random);

        var seaLevel = context.Terrain.SeaLevel;
        if (this.MineshaftType != MineshaftType.Mesa)
            return builder.MoveBelowSeaLevel(seaLevel, context.MinY, random, 10);

        var center = builder.GetBoundingBox().Center;
        var surface = context.Terrain.GetBaseHeight(center.X, center.Z, HeightmapType.WorldSurfaceWG);
        var targetY = surface <= seaLevel ? seaLevel : random.NextInt(surface - seaLevel + 1) + seaLevel;
        var offset = targetY - center.Y;
        builder.OffsetPiecesVertically(offset);
        return offset;
    }
}

/// <summary>
/// Vanilla's <c>MineshaftStructure.Type</c>.
/// </summary>
public enum MineshaftType
{
    Normal,
    Mesa
}

/// <summary>
/// Vanilla's <c>MineshaftPieces</c>: picks and links the pieces of a mineshaft.
/// </summary>
internal static class MineshaftPieces
{
    public const int StartY = 50;

    private const int MaxDepth = 8;

    /// <summary>
    /// Vanilla <c>generateAndAddPiece</c>: adds a random piece at the foot position, then its children, unless the
    /// mineshaft is already too deep or too wide.
    /// </summary>
    public static MineshaftPiece? GenerateAndAddPiece(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random, int x, int y,
        int z, BlockFace direction, int depth)
    {
        if (depth > MaxDepth)
            return null;

        if (Math.Abs(x - start.BoundingBox.MinX) > 80 || Math.Abs(z - start.BoundingBox.MinZ) > 80)
            return null;

        var piece = CreateRandomShaftPiece(pieces, random, x, y, z, direction, depth + 1, ((MineshaftPiece)start).Type);
        if (piece is not null)
        {
            pieces.AddPiece(piece);
            piece.AddChildren(start, pieces, random);
        }

        return piece;
    }

    private static MineshaftPiece? CreateRandomShaftPiece(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z,
        BlockFace direction, int genDepth, MineshaftType type)
    {
        var selection = random.NextInt(100);
        if (selection >= 80)
        {
            var box = MineshaftCrossing.FindCrossing(pieces, random, x, y, z, direction);
            return box is null ? null : new MineshaftCrossing(genDepth, box.Value, direction, type);
        }

        if (selection >= 70)
        {
            var box = MineshaftStairs.FindStairs(pieces, x, y, z, direction);
            return box is null ? null : new MineshaftStairs(genDepth, box.Value, direction, type);
        }

        var corridor = MineshaftCorridor.FindCorridorSize(pieces, random, x, y, z, direction);
        return corridor is null ? null : new MineshaftCorridor(genDepth, random, corridor.Value, direction, type);
    }

    /// <summary>
    /// Vanilla's <c>BoundingBox</c> switch on a direction: the box facing north, mirrored or rotated for the others.
    /// </summary>
    public static BlockBox Facing(BlockFace direction, int x, int y, int z, BlockBox north, BlockBox south, BlockBox west, BlockBox east) =>
        (direction switch
        {
            BlockFace.South => south,
            BlockFace.West => west,
            BlockFace.East => east,
            _ => north
        }).Move(x, y, z);
}

/// <summary>
/// Vanilla's <c>MineshaftPieces.MineShaftPiece</c>: the shared checks of mineshaft pieces.
/// </summary>
public abstract class MineshaftPiece : StructurePiece
{
    private static readonly BiomeSet blockingBiomes = new("#minecraft:mineshaft_blocking");

    protected static readonly IBlock caveAir = BlocksRegistry.Get(Material.CaveAir);

    protected MineshaftPiece(int genDepth, MineshaftType type, BlockBox boundingBox) : base(genDepth, boundingBox)
    {
        this.Type = type;
        (this.Wood, this.Planks, this.Fence) = type == MineshaftType.Mesa
            ? (BlocksRegistry.Get(Material.DarkOakLog), BlocksRegistry.Get(Material.DarkOakPlanks), BlocksRegistry.Get(Material.DarkOakFence))
            : (BlocksRegistry.Get(Material.OakLog), BlocksRegistry.Get(Material.OakPlanks), BlocksRegistry.Get(Material.OakFence));
    }

    public MineshaftType Type { get; }

    protected IBlock Wood { get; }

    protected IBlock Planks { get; }

    protected IBlock Fence { get; }

    // Pieces don't carve through the supports of the pieces placed before them.
    protected override bool CanBeReplaced(IWorldGenLevel level, int x, int y, int z, BlockBox box)
    {
        var material = this.GetBlock(level, x, y, z, box).Material;
        return material != this.Planks.Material && material != this.Wood.Material && material != this.Fence.Material
            && material != Material.IronChain;
    }

    /// <summary>
    /// Vanilla <c>isSupportingBox</c>: whether every block above the support beam is solid.
    /// </summary>
    protected bool IsSupportingBox(IWorldGenLevel level, BlockBox box, int minX, int maxX, int y, int z)
    {
        for (var x = minX; x <= maxX; x++)
        {
            if (this.GetBlock(level, x, y + 1, z, box).IsAir)
                return false;
        }

        return true;
    }

    /// <summary>
    /// Vanilla <c>isInInvalidLocation</c>: the piece isn't placed in blocking biomes (deep dark) or next to water and
    /// lava.
    /// </summary>
    protected bool IsInInvalidLocation(IWorldGenLevel level, BlockBox box)
    {
        var minX = Math.Max(this.BoundingBox.MinX - 1, box.MinX);
        var minY = Math.Max(this.BoundingBox.MinY - 1, box.MinY);
        var minZ = Math.Max(this.BoundingBox.MinZ - 1, box.MinZ);
        var maxX = Math.Min(this.BoundingBox.MaxX + 1, box.MaxX);
        var maxY = Math.Min(this.BoundingBox.MaxY + 1, box.MaxY);
        var maxZ = Math.Min(this.BoundingBox.MaxZ + 1, box.MaxZ);
        if (blockingBiomes.Contains(level.GetBiome(new Vector((minX + maxX) / 2, (minY + maxY) / 2, (minZ + maxZ) / 2))))
            return true;

        for (var x = minX; x <= maxX; x++)
        {
            for (var z = minZ; z <= maxZ; z++)
            {
                if (IsLiquid(level, x, minY, z) || IsLiquid(level, x, maxY, z))
                    return true;
            }
        }

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                if (IsLiquid(level, x, y, minZ) || IsLiquid(level, x, y, maxZ))
                    return true;
            }
        }

        for (var z = minZ; z <= maxZ; z++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                if (IsLiquid(level, minX, y, z) || IsLiquid(level, maxX, y, z))
                    return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Vanilla <c>setPlanksBlock</c>: floors the local position with planks unless it already has a sturdy top.
    /// </summary>
    protected void SetPlanksBlock(IWorldGenLevel level, BlockBox box, int x, int y, int z)
    {
        if (!this.IsInterior(level, x, y, z, box))
            return;

        var position = this.GetWorldPos(x, y, z);
        if (!level.GetBlock(position).IsFaceSturdy(BlockFace.Up))
            level.SetBlock(position, this.Planks);
    }

    private static bool IsLiquid(IWorldGenLevel level, int x, int y, int z) => level.GetBlock(new Vector(x, y, z)).IsLiquidBlock();
}

/// <summary>
/// Vanilla's <c>MineshaftPieces.MineShaftRoom</c>: the dirt-floored room the mineshaft starts from.
/// </summary>
public sealed class MineshaftRoom : MineshaftPiece
{
    // The openings carved into the room's walls for the pieces leading out of it.
    private readonly List<BlockBox> entrances = [];

    public MineshaftRoom(int genDepth, IRandomSource random, int x, int z, MineshaftType type)
        : base(genDepth, type, BlockBox.Create(x, MineshaftPieces.StartY, z, x + 7 + random.NextInt(6), 54 + random.NextInt(6), z + 7 + random.NextInt(6)))
    {
    }

    public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
    {
        var box = this.BoundingBox;
        var heightSpace = Math.Max(box.YSpan - 3 - 1, 1);

        for (var position = 0; position < box.XSpan; position += 4)
        {
            position += random.NextInt(box.XSpan);
            if (position + 3 > box.XSpan)
                break;

            var child = MineshaftPieces.GenerateAndAddPiece(start, pieces, random, box.MinX + position, box.MinY + random.NextInt(heightSpace) + 1,
                box.MinZ - 1, BlockFace.North, this.GenDepth);
            if (child is not null)
            {
                var childBox = child.BoundingBox;
                this.entrances.Add(BlockBox.Create(childBox.MinX, childBox.MinY, box.MinZ, childBox.MaxX, childBox.MaxY, box.MinZ + 1));
            }
        }

        for (var position = 0; position < box.XSpan; position += 4)
        {
            position += random.NextInt(box.XSpan);
            if (position + 3 > box.XSpan)
                break;

            var child = MineshaftPieces.GenerateAndAddPiece(start, pieces, random, box.MinX + position, box.MinY + random.NextInt(heightSpace) + 1,
                box.MaxZ + 1, BlockFace.South, this.GenDepth);
            if (child is not null)
            {
                var childBox = child.BoundingBox;
                this.entrances.Add(BlockBox.Create(childBox.MinX, childBox.MinY, box.MaxZ - 1, childBox.MaxX, childBox.MaxY, box.MaxZ));
            }
        }

        for (var position = 0; position < box.ZSpan; position += 4)
        {
            position += random.NextInt(box.ZSpan);
            if (position + 3 > box.ZSpan)
                break;

            var child = MineshaftPieces.GenerateAndAddPiece(start, pieces, random, box.MinX - 1, box.MinY + random.NextInt(heightSpace) + 1,
                box.MinZ + position, BlockFace.West, this.GenDepth);
            if (child is not null)
            {
                var childBox = child.BoundingBox;
                this.entrances.Add(BlockBox.Create(box.MinX, childBox.MinY, childBox.MinZ, box.MinX + 1, childBox.MaxY, childBox.MaxZ));
            }
        }

        for (var position = 0; position < box.ZSpan; position += 4)
        {
            position += random.NextInt(box.ZSpan);
            if (position + 3 > box.ZSpan)
                break;

            var child = MineshaftPieces.GenerateAndAddPiece(start, pieces, random, box.MaxX + 1, box.MinY + random.NextInt(heightSpace) + 1,
                box.MinZ + position, BlockFace.East, this.GenDepth);
            if (child is not null)
            {
                var childBox = child.BoundingBox;
                this.entrances.Add(BlockBox.Create(box.MaxX - 1, childBox.MinY, childBox.MinZ, box.MaxX, childBox.MaxY, childBox.MaxZ));
            }
        }
    }

    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var box = context.Box;
        if (this.IsInInvalidLocation(level, box))
            return;

        var bounds = this.BoundingBox;
        this.GenerateBox(level, box, bounds.MinX, bounds.MinY + 1, bounds.MinZ, bounds.MaxX, Math.Min(bounds.MinY + 3, bounds.MaxY), bounds.MaxZ,
            caveAir, caveAir, false);

        foreach (var entrance in this.entrances)
        {
            this.GenerateBox(level, box, entrance.MinX, entrance.MaxY - 2, entrance.MinZ, entrance.MaxX, entrance.MaxY, entrance.MaxZ,
                caveAir, caveAir, false);
        }

        this.GenerateUpperHalfSphere(level, box, bounds.MinX, bounds.MinY + 4, bounds.MinZ, bounds.MaxX, bounds.MaxY, bounds.MaxZ, caveAir, false);
    }

    public override void Move(int x, int y, int z)
    {
        base.Move(x, y, z);

        for (var i = 0; i < this.entrances.Count; i++)
            this.entrances[i] = this.entrances[i].Move(x, y, z);
    }
}

/// <summary>
/// Vanilla's <c>MineshaftPieces.MineShaftCorridor</c>: a straight tunnel with supports every 5 blocks, sometimes with
/// rails, minecart chests or a cave spider spawner among cobwebs.
/// </summary>
public sealed class MineshaftCorridor : MineshaftPiece
{
    private readonly bool hasRails;
    private readonly bool spiderCorridor;
    private readonly int numSections;

    // Like vanilla, set by whichever chunk places the spawner first.
    private bool hasPlacedSpider;

    public MineshaftCorridor(int genDepth, IRandomSource random, BlockBox boundingBox, BlockFace direction, MineshaftType type)
        : base(genDepth, type, boundingBox)
    {
        this.Orientation = direction;
        this.hasRails = random.NextInt(3) == 0;
        this.spiderCorridor = !this.hasRails && random.NextInt(23) == 0;
        this.numSections = (direction is BlockFace.North or BlockFace.South ? boundingBox.ZSpan : boundingBox.XSpan) / 5;
    }

    /// <summary>
    /// Vanilla <c>findCorridorSize</c>: the longest free corridor of 2 to 4 sections (tried from a random length down), or
    /// <c>null</c>.
    /// </summary>
    public static BlockBox? FindCorridorSize(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction)
    {
        for (var sections = random.NextInt(3) + 2; sections > 0; sections--)
        {
            var length = sections * 5;
            var box = MineshaftPieces.Facing(direction, x, y, z,
                BlockBox.Create(0, 0, -(length - 1), 2, 2, 0),
                BlockBox.Create(0, 0, 0, 2, 2, length - 1),
                BlockBox.Create(-(length - 1), 0, 0, 0, 2, 2),
                BlockBox.Create(0, 0, 0, length - 1, 2, 2));

            if (pieces.FindCollisionPiece(box) is null)
                return box;
        }

        return null;
    }

    public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
    {
        var depth = this.GenDepth;
        var end = random.NextInt(4);
        var box = this.BoundingBox;

        // The corridor continues ahead, or turns left or right, from its far end.
        switch (this.Orientation)
        {
            case BlockFace.South:
                if (end <= 1)
                    Add(box.MinX, box.MaxZ + 1, BlockFace.South);
                else if (end == 2)
                    Add(box.MinX - 1, box.MaxZ - 3, BlockFace.West);
                else
                    Add(box.MaxX + 1, box.MaxZ - 3, BlockFace.East);
                break;
            case BlockFace.West:
                if (end <= 1)
                    Add(box.MinX - 1, box.MinZ, BlockFace.West);
                else if (end == 2)
                    Add(box.MinX, box.MinZ - 1, BlockFace.North);
                else
                    Add(box.MinX, box.MaxZ + 1, BlockFace.South);
                break;
            case BlockFace.East:
                if (end <= 1)
                    Add(box.MaxX + 1, box.MinZ, BlockFace.East);
                else if (end == 2)
                    Add(box.MaxX - 3, box.MinZ - 1, BlockFace.North);
                else
                    Add(box.MaxX - 3, box.MaxZ + 1, BlockFace.South);
                break;
            default:
                if (end <= 1)
                    Add(box.MinX, box.MinZ - 1, BlockFace.North);
                else if (end == 2)
                    Add(box.MinX - 1, box.MinZ, BlockFace.West);
                else
                    Add(box.MaxX + 1, box.MinZ, BlockFace.East);
                break;
        }

        if (depth >= 8)
            return;

        // Side branches every 5 blocks.
        if (this.Orientation is BlockFace.North or BlockFace.South)
        {
            for (var z = box.MinZ + 3; z + 3 <= box.MaxZ; z += 5)
            {
                var selection = random.NextInt(5);
                if (selection == 0)
                    MineshaftPieces.GenerateAndAddPiece(start, pieces, random, box.MinX - 1, box.MinY, z, BlockFace.West, depth + 1);
                else if (selection == 1)
                    MineshaftPieces.GenerateAndAddPiece(start, pieces, random, box.MaxX + 1, box.MinY, z, BlockFace.East, depth + 1);
            }
        }
        else
        {
            for (var x = box.MinX + 3; x + 3 <= box.MaxX; x += 5)
            {
                var selection = random.NextInt(5);
                if (selection == 0)
                    MineshaftPieces.GenerateAndAddPiece(start, pieces, random, x, box.MinY, box.MinZ - 1, BlockFace.North, depth + 1);
                else if (selection == 1)
                    MineshaftPieces.GenerateAndAddPiece(start, pieces, random, x, box.MinY, box.MaxZ + 1, BlockFace.South, depth + 1);
            }
        }

        void Add(int x, int z, BlockFace direction) =>
            MineshaftPieces.GenerateAndAddPiece(start, pieces, random, x, box.MinY - 1 + random.NextInt(3), z, direction, depth);
    }

    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var box = context.Box;
        var random = context.Random;
        if (this.IsInInvalidLocation(level, box))
            return;

        var length = this.numSections * 5 - 1;
        this.GenerateBox(level, box, 0, 0, 0, 2, 1, length, caveAir, caveAir, false);
        this.GenerateMaybeBox(level, box, random, 0.8f, 0, 2, 0, 2, 2, length, caveAir, caveAir, false, false);
        if (this.spiderCorridor)
            this.GenerateMaybeBox(level, box, random, 0.6f, 0, 0, 0, 2, 1, length, BlocksRegistry.Get(Material.Cobweb), caveAir, false, true);

        for (var section = 0; section < this.numSections; section++)
        {
            var z = 2 + section * 5;
            this.PlaceSupport(level, box, 0, 0, z, 2, 2, random);
            this.MaybePlaceCobweb(level, box, random, 0.1f, 0, 2, z - 1);
            this.MaybePlaceCobweb(level, box, random, 0.1f, 2, 2, z - 1);
            this.MaybePlaceCobweb(level, box, random, 0.1f, 0, 2, z + 1);
            this.MaybePlaceCobweb(level, box, random, 0.1f, 2, 2, z + 1);
            this.MaybePlaceCobweb(level, box, random, 0.05f, 0, 2, z - 2);
            this.MaybePlaceCobweb(level, box, random, 0.05f, 2, 2, z - 2);
            this.MaybePlaceCobweb(level, box, random, 0.05f, 0, 2, z + 2);
            this.MaybePlaceCobweb(level, box, random, 0.05f, 2, 2, z + 2);
            if (random.NextInt(100) == 0)
                this.CreateMinecartChest(level, box, random, 2, 0, z - 1);

            if (random.NextInt(100) == 0)
                this.CreateMinecartChest(level, box, random, 0, 0, z + 1);

            if (this.spiderCorridor && !this.hasPlacedSpider)
            {
                var spawnerZ = z - 1 + random.NextInt(3);
                var position = this.GetWorldPos(1, 0, spawnerZ);
                if (box.IsInside(position) && this.IsInterior(level, 1, 0, spawnerZ, box))
                {
                    this.hasPlacedSpider = true;
                    level.SetBlock(position, BlocksRegistry.Get(Material.Spawner));
                    FeatureHelpers.SetSpawnerEntity(level, position, () => "minecraft:cave_spider");
                }
            }
        }

        for (var x = 0; x <= 2; x++)
        {
            for (var z = 0; z <= length; z++)
                this.SetPlanksBlock(level, box, x, -1, z);
        }

        this.PlaceDoubleLowerOrUpperSupport(level, box, 0, -1, 2);
        if (this.numSections > 1)
            this.PlaceDoubleLowerOrUpperSupport(level, box, 0, -1, length - 2);

        if (!this.hasRails)
            return;

        var rail = BlocksRegistry.Get(Material.Rail).WithProperty("shape", "north_south");
        for (var z = 0; z <= length; z++)
        {
            var floor = this.GetBlock(level, 1, -1, z, box);
            if (!floor.IsAir && floor.IsSolidRender())
                this.MaybeGenerateBlock(level, box, random, this.IsInterior(level, 1, 0, z, box) ? 0.7f : 0.9f, 1, 0, z, rail);
        }
    }

    /// <summary>
    /// Vanilla's <c>createChest</c> override: a chest minecart on a rail, if the local position is air on solid ground.
    /// </summary>
    private void CreateMinecartChest(IWorldGenLevel level, BlockBox box, IRandomSource random, int x, int y, int z)
    {
        var position = this.GetWorldPos(x, y, z);
        if (!box.IsInside(position) || !level.GetBlock(position).IsAir || level.GetBlock(position + Vector.Down).IsAir)
            return;

        var shape = random.NextBoolean() ? "north_south" : "east_west";
        this.PlaceBlock(level, BlocksRegistry.Get(Material.Rail).WithProperty("shape", shape), x, y, z, box);

        var minecart = new GeneratedEntity("minecraft:chest_minecart", new VectorF(position.X + 0.5f, position.Y + 0.5f, position.Z + 0.5f))
        {
            Data = { new NbtTag<string>("LootTable", "minecraft:chests/abandoned_mineshaft") }
        };

        // Like vanilla, a zero seed isn't saved.
        var seed = random.NextLong();
        if (seed != 0L)
            minecart.Data.Add(new NbtTag<long>("LootTableSeed", seed));

        level.AddEntity(minecart);
    }

    private void PlaceDoubleLowerOrUpperSupport(IWorldGenLevel level, BlockBox box, int x, int y, int z)
    {
        if (this.GetBlock(level, x, y, z, box).Material == this.Planks.Material)
            this.FillPillarDownOrChainUp(level, x, y, z, box);

        if (this.GetBlock(level, x + 2, y, z, box).Material == this.Planks.Material)
            this.FillPillarDownOrChainUp(level, x + 2, y, z, box);
    }

    /// <summary>
    /// Vanilla <c>fillPillarDownOrChainUp</c>: props the floor with a log pillar down to sturdy ground (up to 20 blocks), or
    /// hangs it from a fence and chains up to a ceiling (up to 50 blocks), whichever is found first.
    /// </summary>
    private void FillPillarDownOrChainUp(IWorldGenLevel level, int x, int y, int z, BlockBox box)
    {
        var position = this.GetWorldPos(x, y, z);
        if (!box.IsInside(position))
            return;

        var worldY = position.Y;
        var checkBelow = true;
        var checkAbove = true;
        for (var distance = 1; checkBelow || checkAbove; distance++)
        {
            if (checkBelow)
            {
                var below = level.GetBlock(position with { Y = worldY - distance });
                var emptyBelow = IsReplaceableByStructures(below) && below.Material != Material.Lava;
                if (!emptyBelow && below.IsFaceSturdy(BlockFace.Up))
                {
                    FillColumnBetween(level, this.Wood, position, worldY - distance + 1, worldY);
                    return;
                }

                checkBelow = distance <= 20 && emptyBelow && worldY - distance > level.MinY + 1;
            }

            if (checkAbove)
            {
                var above = level.GetBlock(position with { Y = worldY + distance });
                var emptyAbove = IsReplaceableByStructures(above);
                if (!emptyAbove && above.IsBottomCenterSturdy() && !above.IsFallingBlock())
                {
                    level.SetBlock(position with { Y = worldY + 1 }, this.Fence);
                    FillColumnBetween(level, BlocksRegistry.Get(Material.IronChain), position, worldY + 2, worldY + distance);
                    return;
                }

                checkAbove = distance <= 50 && emptyAbove && worldY + distance < level.MinY + level.Height - 1;
            }
        }
    }

    private static void FillColumnBetween(IWorldGenLevel level, IBlock block, Vector position, int minY, int maxYExclusive)
    {
        for (var y = minY; y < maxYExclusive; y++)
            level.SetBlock(position with { Y = y }, block);
    }

    /// <summary>
    /// Vanilla <c>placeSupport</c>: two fence posts under a plank beam (or two plank caps), with an occasional torch,
    /// where the ceiling above is solid.
    /// </summary>
    private void PlaceSupport(IWorldGenLevel level, BlockBox box, int minX, int minY, int z, int maxY, int maxX, IRandomSource random)
    {
        if (!this.IsSupportingBox(level, box, minX, maxX, maxY, z))
            return;

        this.GenerateBox(level, box, minX, minY, z, minX, maxY - 1, z, this.Fence.WithProperty("west", true), caveAir, false);
        this.GenerateBox(level, box, maxX, minY, z, maxX, maxY - 1, z, this.Fence.WithProperty("east", true), caveAir, false);
        if (random.NextInt(4) == 0)
        {
            this.GenerateBox(level, box, minX, maxY, z, minX, maxY, z, this.Planks, caveAir, false);
            this.GenerateBox(level, box, maxX, maxY, z, maxX, maxY, z, this.Planks, caveAir, false);
            return;
        }

        this.GenerateBox(level, box, minX, maxY, z, maxX, maxY, z, this.Planks, caveAir, false);
        var torch = BlocksRegistry.Get(Material.WallTorch);
        this.MaybeGenerateBlock(level, box, random, 0.05f, minX + 1, maxY, z - 1, torch.WithProperty("facing", "south"));
        this.MaybeGenerateBlock(level, box, random, 0.05f, minX + 1, maxY, z + 1, torch.WithProperty("facing", "north"));
    }

    private void MaybePlaceCobweb(IWorldGenLevel level, BlockBox box, IRandomSource random, float probability, int x, int y, int z)
    {
        if (this.IsInterior(level, x, y, z, box) && random.NextFloat() < probability && this.HasSturdyNeighbours(level, box, x, y, z, 2))
            this.PlaceBlock(level, BlocksRegistry.Get(Material.Cobweb), x, y, z, box);
    }

    private bool HasSturdyNeighbours(IWorldGenLevel level, BlockBox box, int x, int y, int z, int count)
    {
        var position = this.GetWorldPos(x, y, z);
        var sturdy = 0;
        foreach (var direction in FeatureHelpers.Directions)
        {
            var neighbor = position.Offset(direction);
            if (box.IsInside(neighbor) && level.GetBlock(neighbor).IsFaceSturdy(direction.Opposite()) && ++sturdy >= count)
                return true;
        }

        return false;
    }
}

/// <summary>
/// Vanilla's <c>MineshaftPieces.MineShaftCrossing</c>: a junction, sometimes two floors high, with plank pillars.
/// </summary>
public sealed class MineshaftCrossing : MineshaftPiece
{
    private readonly BlockFace direction;
    private readonly bool isTwoFloored;

    public MineshaftCrossing(int genDepth, BlockBox boundingBox, BlockFace direction, MineshaftType type) : base(genDepth, type, boundingBox)
    {
        this.direction = direction;
        this.isTwoFloored = boundingBox.YSpan > 3;
    }

    /// <summary>
    /// Vanilla <c>findCrossing</c>: a free crossing box (two floors high one time in four), or <c>null</c>.
    /// </summary>
    public static BlockBox? FindCrossing(IStructurePieceAccessor pieces, IRandomSource random, int x, int y, int z, BlockFace direction)
    {
        var maxY = random.NextInt(4) == 0 ? 6 : 2;
        var box = MineshaftPieces.Facing(direction, x, y, z,
            BlockBox.Create(-1, 0, -4, 3, maxY, 0),
            BlockBox.Create(-1, 0, 0, 3, maxY, 4),
            BlockBox.Create(-4, 0, -1, 0, maxY, 3),
            BlockBox.Create(0, 0, -1, 4, maxY, 3));

        return pieces.FindCollisionPiece(box) is null ? box : null;
    }

    public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
    {
        var depth = this.GenDepth;
        var box = this.BoundingBox;

        // Every side but the one leading back.
        switch (this.direction)
        {
            case BlockFace.South:
                Add(box.MinX + 1, box.MinY, box.MaxZ + 1, BlockFace.South);
                Add(box.MinX - 1, box.MinY, box.MinZ + 1, BlockFace.West);
                Add(box.MaxX + 1, box.MinY, box.MinZ + 1, BlockFace.East);
                break;
            case BlockFace.West:
                Add(box.MinX + 1, box.MinY, box.MinZ - 1, BlockFace.North);
                Add(box.MinX + 1, box.MinY, box.MaxZ + 1, BlockFace.South);
                Add(box.MinX - 1, box.MinY, box.MinZ + 1, BlockFace.West);
                break;
            case BlockFace.East:
                Add(box.MinX + 1, box.MinY, box.MinZ - 1, BlockFace.North);
                Add(box.MinX + 1, box.MinY, box.MaxZ + 1, BlockFace.South);
                Add(box.MaxX + 1, box.MinY, box.MinZ + 1, BlockFace.East);
                break;
            default:
                Add(box.MinX + 1, box.MinY, box.MinZ - 1, BlockFace.North);
                Add(box.MinX - 1, box.MinY, box.MinZ + 1, BlockFace.West);
                Add(box.MaxX + 1, box.MinY, box.MinZ + 1, BlockFace.East);
                break;
        }

        if (!this.isTwoFloored)
            return;

        var upperY = box.MinY + 3 + 1;
        if (random.NextBoolean())
            Add(box.MinX + 1, upperY, box.MinZ - 1, BlockFace.North);

        if (random.NextBoolean())
            Add(box.MinX - 1, upperY, box.MinZ + 1, BlockFace.West);

        if (random.NextBoolean())
            Add(box.MaxX + 1, upperY, box.MinZ + 1, BlockFace.East);

        if (random.NextBoolean())
            Add(box.MinX + 1, upperY, box.MaxZ + 1, BlockFace.South);

        void Add(int x, int y, int z, BlockFace direction) =>
            MineshaftPieces.GenerateAndAddPiece(start, pieces, random, x, y, z, direction, depth);
    }

    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var box = context.Box;
        if (this.IsInInvalidLocation(level, box))
            return;

        var bounds = this.BoundingBox;
        if (this.isTwoFloored)
        {
            this.GenerateBox(level, box, bounds.MinX + 1, bounds.MinY, bounds.MinZ, bounds.MaxX - 1, bounds.MinY + 3 - 1, bounds.MaxZ,
                caveAir, caveAir, false);
            this.GenerateBox(level, box, bounds.MinX, bounds.MinY, bounds.MinZ + 1, bounds.MaxX, bounds.MinY + 3 - 1, bounds.MaxZ - 1,
                caveAir, caveAir, false);
            this.GenerateBox(level, box, bounds.MinX + 1, bounds.MaxY - 2, bounds.MinZ, bounds.MaxX - 1, bounds.MaxY, bounds.MaxZ,
                caveAir, caveAir, false);
            this.GenerateBox(level, box, bounds.MinX, bounds.MaxY - 2, bounds.MinZ + 1, bounds.MaxX, bounds.MaxY, bounds.MaxZ - 1,
                caveAir, caveAir, false);
            this.GenerateBox(level, box, bounds.MinX + 1, bounds.MinY + 3, bounds.MinZ + 1, bounds.MaxX - 1, bounds.MinY + 3, bounds.MaxZ - 1,
                caveAir, caveAir, false);
        }
        else
        {
            this.GenerateBox(level, box, bounds.MinX + 1, bounds.MinY, bounds.MinZ, bounds.MaxX - 1, bounds.MaxY, bounds.MaxZ,
                caveAir, caveAir, false);
            this.GenerateBox(level, box, bounds.MinX, bounds.MinY, bounds.MinZ + 1, bounds.MaxX, bounds.MaxY, bounds.MaxZ - 1,
                caveAir, caveAir, false);
        }

        this.PlaceSupportPillar(level, box, bounds.MinX + 1, bounds.MinY, bounds.MinZ + 1, bounds.MaxY);
        this.PlaceSupportPillar(level, box, bounds.MinX + 1, bounds.MinY, bounds.MaxZ - 1, bounds.MaxY);
        this.PlaceSupportPillar(level, box, bounds.MaxX - 1, bounds.MinY, bounds.MinZ + 1, bounds.MaxY);
        this.PlaceSupportPillar(level, box, bounds.MaxX - 1, bounds.MinY, bounds.MaxZ - 1, bounds.MaxY);

        for (var x = bounds.MinX; x <= bounds.MaxX; x++)
        {
            for (var z = bounds.MinZ; z <= bounds.MaxZ; z++)
                this.SetPlanksBlock(level, box, x, bounds.MinY - 1, z);
        }
    }

    private void PlaceSupportPillar(IWorldGenLevel level, BlockBox box, int x, int minY, int z, int maxY)
    {
        if (!this.GetBlock(level, x, maxY + 1, z, box).IsAir)
            this.GenerateBox(level, box, x, minY, z, x, maxY, z, this.Planks, caveAir, false);
    }
}

/// <summary>
/// Vanilla's <c>MineshaftPieces.MineShaftStairs</c>: a tunnel going down 5 blocks.
/// </summary>
public sealed class MineshaftStairs : MineshaftPiece
{
    public MineshaftStairs(int genDepth, BlockBox boundingBox, BlockFace direction, MineshaftType type) : base(genDepth, type, boundingBox)
    {
        this.Orientation = direction;
    }

    /// <summary>
    /// Vanilla <c>findStairs</c>: the stairs box if it's free, or <c>null</c>.
    /// </summary>
    public static BlockBox? FindStairs(IStructurePieceAccessor pieces, int x, int y, int z, BlockFace direction)
    {
        var box = MineshaftPieces.Facing(direction, x, y, z,
            BlockBox.Create(0, -5, -8, 2, 2, 0),
            BlockBox.Create(0, -5, 0, 2, 2, 8),
            BlockBox.Create(-8, -5, 0, 0, 2, 2),
            BlockBox.Create(0, -5, 0, 8, 2, 2));

        return pieces.FindCollisionPiece(box) is null ? box : null;
    }

    public override void AddChildren(StructurePiece start, IStructurePieceAccessor pieces, IRandomSource random)
    {
        var box = this.BoundingBox;
        var (x, z, direction) = this.Orientation switch
        {
            BlockFace.South => (box.MinX, box.MaxZ + 1, BlockFace.South),
            BlockFace.West => (box.MinX - 1, box.MinZ, BlockFace.West),
            BlockFace.East => (box.MaxX + 1, box.MinZ, BlockFace.East),
            _ => (box.MinX, box.MinZ - 1, BlockFace.North)
        };

        MineshaftPieces.GenerateAndAddPiece(start, pieces, random, x, box.MinY, z, direction, this.GenDepth);
    }

    public override void PostProcess(StructurePieceContext context)
    {
        var level = context.Level;
        var box = context.Box;
        if (this.IsInInvalidLocation(level, box))
            return;

        this.GenerateBox(level, box, 0, 5, 0, 2, 7, 1, caveAir, caveAir, false);
        this.GenerateBox(level, box, 0, 0, 7, 2, 2, 8, caveAir, caveAir, false);

        for (var i = 0; i < 5; i++)
            this.GenerateBox(level, box, 0, 5 - i - (i < 4 ? 1 : 0), 2 + i, 2, 7 - i, 2 + i, caveAir, caveAir, false);
    }
}
