using Obsidian.API.World.Generator.RandomSources;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures;

/// <summary>
/// The pieces of an ocean monument, like vanilla's <c>OceanMonumentPieces</c>. Piece types keep vanilla's names.
/// </summary>
/// <remarks>
/// The structure is a single <see cref="MonumentBuilding"/> piece. It lays out a 5x5x3 grid of 8x4x8 rooms when created
/// and places the rooms itself, so they aren't pieces of the structure start (like vanilla).
/// </remarks>
public static class OceanMonumentPieces
{
    // Vanilla's Direction 3D data values, which index a room's connections and openings.
    private const int Down = 0;
    private const int Up = 1;
    private const int North = 2;
    private const int South = 3;
    private const int West = 4;
    private const int East = 5;

    private static readonly int[] stepX = [0, 0, 0, 0, -1, 1];
    private static readonly int[] stepY = [-1, 1, 0, 0, 0, 0];
    private static readonly int[] stepZ = [0, 0, -1, 1, 0, 0];

    private static readonly IBlock baseGray = BlocksRegistry.Get(Material.Prismarine);
    private static readonly IBlock baseLight = BlocksRegistry.Get(Material.PrismarineBricks);
    private static readonly IBlock baseBlack = BlocksRegistry.Get(Material.DarkPrismarine);
    private static readonly IBlock lamp = BlocksRegistry.Get(Material.SeaLantern);
    private static readonly IBlock water = BlocksRegistry.Get(Material.Water);

    private static readonly int sourceRoomIndex = GetRoomIndex(2, 0, 0);
    private static readonly int topConnectIndex = GetRoomIndex(2, 2, 0);
    private static readonly int leftWingConnectIndex = GetRoomIndex(0, 1, 0);
    private static readonly int rightWingConnectIndex = GetRoomIndex(4, 1, 0);

    // Vanilla's fitters, tried in order for each unclaimed room: the first that fits builds the room.
    private static readonly RoomFitter[] fitters =
    [
        new(FitsDoubleXY, (orientation, room, _) =>
        {
            room.Claimed = true;
            room.Connections[East]!.Claimed = true;
            room.Connections[Up]!.Claimed = true;
            room.Connections[East]!.Connections[Up]!.Claimed = true;
            return new OceanMonumentDoubleXYRoom(orientation, room);
        }),
        new(FitsDoubleYZ, (orientation, room, _) =>
        {
            room.Claimed = true;
            room.Connections[North]!.Claimed = true;
            room.Connections[Up]!.Claimed = true;
            room.Connections[North]!.Connections[Up]!.Claimed = true;
            return new OceanMonumentDoubleYZRoom(orientation, room);
        }),
        new(room => room.HasOpening[North] && !room.Connections[North]!.Claimed, (orientation, room, _) =>
        {
            // Vanilla falls back to the room to the south when the north one is taken, which the fit check rules out.
            room.Claimed = true;
            room.Connections[North]!.Claimed = true;
            return new OceanMonumentDoubleZRoom(orientation, room);
        }),
        new(room => room.HasOpening[East] && !room.Connections[East]!.Claimed, (orientation, room, _) =>
        {
            room.Claimed = true;
            room.Connections[East]!.Claimed = true;
            return new OceanMonumentDoubleXRoom(orientation, room);
        }),
        new(room => room.HasOpening[Up] && !room.Connections[Up]!.Claimed, (orientation, room, _) =>
        {
            room.Claimed = true;
            room.Connections[Up]!.Claimed = true;
            return new OceanMonumentDoubleYRoom(orientation, room);
        }),
        new(room => !room.HasOpening[West] && !room.HasOpening[East] && !room.HasOpening[North] && !room.HasOpening[South] && !room.HasOpening[Up],
            (orientation, room, _) =>
            {
                room.Claimed = true;
                return new OceanMonumentSimpleTopRoom(orientation, room);
            }),
        new(_ => true, (orientation, room, random) =>
        {
            room.Claimed = true;
            return new OceanMonumentSimpleRoom(orientation, room, random);
        })
    ];

    private static int GetRoomIndex(int roomX, int roomY, int roomZ) => roomY * 25 + roomZ * 5 + roomX;

    private static int Opposite(int direction) => direction ^ 1;

    private static bool FitsDoubleXY(RoomDefinition room)
    {
        if (!room.HasOpening[East] || room.Connections[East]!.Claimed || !room.HasOpening[Up] || room.Connections[Up]!.Claimed)
            return false;

        var east = room.Connections[East]!;
        return east.HasOpening[Up] && !east.Connections[Up]!.Claimed;
    }

    private static bool FitsDoubleYZ(RoomDefinition room)
    {
        if (!room.HasOpening[North] || room.Connections[North]!.Claimed || !room.HasOpening[Up] || room.Connections[Up]!.Claimed)
            return false;

        var north = room.Connections[North]!;
        return north.HasOpening[Up] && !north.Connections[Up]!.Claimed;
    }

    private sealed record RoomFitter(Func<RoomDefinition, bool> Fits, Func<BlockFace, RoomDefinition, IRandomSource, OceanMonumentPiece> Create);

    /// <summary>
    /// Vanilla's <c>RoomDefinition</c>: a cell of the room grid (or a wing or the penthouse) and its links to its neighbors.
    /// </summary>
    internal sealed class RoomDefinition(int index)
    {
        /// <summary>
        /// The grid index (<see cref="GetRoomIndex"/>), or 1001 to 1003 for the wings and the penthouse.
        /// </summary>
        public int Index => index;

        public RoomDefinition?[] Connections { get; } = new RoomDefinition?[6];

        public bool[] HasOpening { get; } = new bool[6];

        /// <summary>
        /// Whether a piece was built over the room.
        /// </summary>
        public bool Claimed { get; set; }

        public bool IsSource { get; set; }

        private int scanIndex;

        public bool IsSpecial => index >= 75;

        public int OpeningCount => this.HasOpening.Count(opening => opening);

        public void SetConnection(int direction, RoomDefinition room)
        {
            this.Connections[direction] = room;
            room.Connections[Opposite(direction)] = this;
        }

        public void UpdateOpenings()
        {
            for (var i = 0; i < 6; i++)
                this.HasOpening[i] = this.Connections[i] is not null;
        }

        /// <summary>
        /// Vanilla <c>findSource</c>: whether the entrance room can be reached through open connections.
        /// </summary>
        public bool FindSource(int scanIndex)
        {
            if (this.IsSource)
                return true;

            this.scanIndex = scanIndex;
            for (var i = 0; i < 6; i++)
            {
                var connection = this.Connections[i];
                if (connection is not null && this.HasOpening[i] && connection.scanIndex != scanIndex && connection.FindSource(scanIndex))
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentPiece</c>: a part of the monument, with its block palette and water filling.
    /// </summary>
    public abstract class OceanMonumentPiece : StructurePiece
    {
        protected OceanMonumentPiece(BlockFace orientation, int genDepth, BlockBox boundingBox) : base(genDepth, boundingBox)
        {
            this.Orientation = orientation;
        }

        /// <summary>
        /// A piece covering <paramref name="roomWidth"/> by <paramref name="roomHeight"/> by <paramref name="roomDepth"/>
        /// grid rooms from <paramref name="room"/>, relative to the room grid's corner.
        /// </summary>
        private protected OceanMonumentPiece(int genDepth, BlockFace orientation, RoomDefinition room, int roomWidth, int roomHeight, int roomDepth)
            : base(genDepth, MakeRoomBoundingBox(orientation, room, roomWidth, roomHeight, roomDepth))
        {
            this.Orientation = orientation;
            this.Room = room;
        }

        private protected RoomDefinition Room { get; } = null!;

        private static BlockBox MakeRoomBoundingBox(BlockFace orientation, RoomDefinition room, int roomWidth, int roomHeight, int roomDepth)
        {
            var roomX = room.Index % 5;
            var roomZ = room.Index / 5 % 5;
            var roomY = room.Index / 25;
            var box = MakeBoundingBox(0, 0, 0, orientation, roomWidth * 8, roomHeight * 4, roomDepth * 8);
            return orientation switch
            {
                BlockFace.North => box.Move(roomX * 8, roomY * 4, -(roomZ + roomDepth) * 8 + 1),
                BlockFace.South => box.Move(roomX * 8, roomY * 4, roomZ * 8),
                BlockFace.West => box.Move(-(roomZ + roomDepth) * 8 + 1, roomY * 4, roomX * 8),
                _ => box.Move(roomZ * 8, roomY * 4, roomX * 8)
            };
        }

        protected void Fill(StructurePieceContext context, int minX, int minY, int minZ, int maxX, int maxY, int maxZ, IBlock block) =>
            this.GenerateBox(context.Level, context.Box, minX, minY, minZ, maxX, maxY, maxZ, block, block, false);

        protected void Place(StructurePieceContext context, IBlock block, int x, int y, int z) =>
            this.PlaceBlock(context.Level, block, x, y, z, context.Box);

        /// <summary>
        /// Vanilla <c>generateWaterBox</c>: fills with water below sea level and air above it, keeping ice and water.
        /// </summary>
        protected void GenerateWaterBox(StructurePieceContext context, int minX, int minY, int minZ, int maxX, int maxY, int maxZ)
        {
            var level = context.Level;
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    for (var z = minZ; z <= maxZ; z++)
                    {
                        // Vanilla also checks for water before placing air, which the kept blocks already rule out.
                        var block = this.GetBlock(level, x, y, z, context.Box);
                        if (block.Material is Material.Ice or Material.PackedIce or Material.BlueIce or Material.Water)
                            continue;

                        this.PlaceBlock(level, this.GetWorldY(y) >= level.SeaLevel ? BlocksRegistry.Air : water, x, y, z, context.Box);
                    }
                }
            }
        }

        /// <summary>
        /// Vanilla <c>generateDefaultFloor</c>: a room's floor, with a hole in the middle when it opens down.
        /// </summary>
        protected void GenerateDefaultFloor(StructurePieceContext context, int xOffset, int zOffset, bool downOpening)
        {
            if (!downOpening)
            {
                this.Fill(context, xOffset, 0, zOffset, xOffset + 7, 0, zOffset + 7, baseGray);
                return;
            }

            this.Fill(context, xOffset, 0, zOffset, xOffset + 2, 0, zOffset + 7, baseGray);
            this.Fill(context, xOffset + 5, 0, zOffset, xOffset + 7, 0, zOffset + 7, baseGray);
            this.Fill(context, xOffset + 3, 0, zOffset, xOffset + 4, 0, zOffset + 2, baseGray);
            this.Fill(context, xOffset + 3, 0, zOffset + 5, xOffset + 4, 0, zOffset + 7, baseGray);
            this.Fill(context, xOffset + 3, 0, zOffset + 2, xOffset + 4, 0, zOffset + 2, baseLight);
            this.Fill(context, xOffset + 3, 0, zOffset + 5, xOffset + 4, 0, zOffset + 5, baseLight);
            this.Fill(context, xOffset + 2, 0, zOffset + 3, xOffset + 2, 0, zOffset + 4, baseLight);
            this.Fill(context, xOffset + 5, 0, zOffset + 3, xOffset + 5, 0, zOffset + 4, baseLight);
        }

        /// <summary>
        /// Vanilla <c>generateBoxOnFillOnly</c>: replaces only the water (default state) in a local box.
        /// </summary>
        protected void GenerateBoxOnFillOnly(StructurePieceContext context, int minX, int minY, int minZ, int maxX, int maxY, int maxZ,
            IBlock block)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                {
                    for (var z = minZ; z <= maxZ; z++)
                    {
                        if (this.GetBlock(context.Level, x, y, z, context.Box).IsSameState(water))
                            this.Place(context, block, x, y, z);
                    }
                }
            }
        }

        /// <summary>
        /// Vanilla <c>chunkIntersects</c>: whether the local horizontal area reaches the context's box.
        /// </summary>
        protected bool ChunkIntersects(StructurePieceContext context, int minX, int minZ, int maxX, int maxZ)
        {
            var x0 = this.GetWorldX(minX, minZ);
            var z0 = this.GetWorldZ(minX, minZ);
            var x1 = this.GetWorldX(maxX, maxZ);
            var z1 = this.GetWorldZ(maxX, maxZ);
            return context.Box.Intersects(Math.Min(x0, x1), Math.Min(z0, z1), Math.Max(x0, x1), Math.Max(z0, z1));
        }

        /// <summary>
        /// Vanilla <c>spawnElder</c>: a persistent elder guardian at the local position, with the chunk that holds it.
        /// </summary>
        protected void SpawnElder(StructurePieceContext context, int x, int y, int z)
        {
            var position = this.GetWorldPos(x, y, z);
            if (!context.Box.IsInside(position))
                return;

            context.Level.AddEntity(new GeneratedEntity("minecraft:elder_guardian", new VectorD(position.X + 0.5, position.Y, position.Z + 0.5))
            {
                Data = { new NbtTag<bool>("PersistenceRequired", true) }
            });
        }
    }

    /// <summary>
    /// Vanilla's <c>MonumentBuilding</c>: the monument's outer shell, which lays out and places its rooms.
    /// </summary>
    public sealed class MonumentBuilding : OceanMonumentPiece
    {
        /// <summary>
        /// How far around the monument's center every biome must allow it.
        /// </summary>
        public const int BiomeRangeCheck = 29;

        private readonly List<OceanMonumentPiece> childPieces = [];
        private RoomDefinition sourceRoom = null!;
        private RoomDefinition coreRoom = null!;

        public MonumentBuilding(IRandomSource random, int west, int north, BlockFace orientation)
            : base(orientation, 0, MakeBoundingBox(west, 39, north, orientation, 58, 23, 58))
        {
            var rooms = this.GenerateRoomGraph(random);
            this.sourceRoom.Claimed = true;
            this.childPieces.Add(new OceanMonumentEntryRoom(orientation, this.sourceRoom));
            this.childPieces.Add(new OceanMonumentCoreRoom(orientation, this.coreRoom));

            foreach (var room in rooms)
            {
                if (room.Claimed || room.IsSpecial)
                    continue;

                foreach (var fitter in fitters)
                {
                    if (!fitter.Fits(room))
                        continue;

                    this.childPieces.Add(fitter.Create(orientation, room, random));
                    break;
                }
            }

            // The rooms were laid out from the grid's corner; move them into the building.
            var offset = this.GetWorldPos(9, 0, 22);
            foreach (var child in this.childPieces)
                child.Move(offset.X, offset.Y, offset.Z);

            var leftWing = BlockBox.FromCorners(this.GetWorldPos(1, 1, 1), this.GetWorldPos(23, 8, 21));
            var rightWing = BlockBox.FromCorners(this.GetWorldPos(34, 1, 1), this.GetWorldPos(56, 8, 21));
            var penthouse = BlockBox.FromCorners(this.GetWorldPos(22, 13, 22), this.GetWorldPos(35, 17, 35));
            var wingRandom = random.NextInt();
            this.childPieces.Add(new OceanMonumentWingRoom(orientation, leftWing, wingRandom++));
            this.childPieces.Add(new OceanMonumentWingRoom(orientation, rightWing, wingRandom));
            this.childPieces.Add(new OceanMonumentPenthouse(orientation, penthouse));
        }

        /// <summary>
        /// Vanilla <c>generateRoomGraph</c>: links the grid rooms, picks the core room and closes random openings while every
        /// room stays reachable from the entrance.
        /// </summary>
        private List<RoomDefinition> GenerateRoomGraph(IRandomSource random)
        {
            var grid = new RoomDefinition?[75];
            for (var x = 0; x < 5; x++)
            {
                for (var z = 0; z < 4; z++)
                    grid[GetRoomIndex(x, 0, z)] = new RoomDefinition(GetRoomIndex(x, 0, z));
            }

            for (var x = 0; x < 5; x++)
            {
                for (var z = 0; z < 4; z++)
                    grid[GetRoomIndex(x, 1, z)] = new RoomDefinition(GetRoomIndex(x, 1, z));
            }

            for (var x = 1; x < 4; x++)
            {
                for (var z = 0; z < 2; z++)
                    grid[GetRoomIndex(x, 2, z)] = new RoomDefinition(GetRoomIndex(x, 2, z));
            }

            this.sourceRoom = grid[sourceRoomIndex]!;

            for (var x = 0; x < 5; x++)
            {
                for (var z = 0; z < 5; z++)
                {
                    for (var y = 0; y < 3; y++)
                    {
                        var room = grid[GetRoomIndex(x, y, z)];
                        if (room is null)
                            continue;

                        for (var direction = 0; direction < 6; direction++)
                        {
                            var neighborX = x + stepX[direction];
                            var neighborY = y + stepY[direction];
                            var neighborZ = z + stepZ[direction];
                            if (neighborX is < 0 or >= 5 || neighborZ is < 0 or >= 5 || neighborY is < 0 or >= 3)
                                continue;

                            var neighbor = grid[GetRoomIndex(neighborX, neighborY, neighborZ)];
                            if (neighbor is null)
                                continue;

                            // Grid Z grows away from the entrance, so vanilla links north and south the other way around.
                            room.SetConnection(neighborZ == z ? direction : Opposite(direction), neighbor);
                        }
                    }
                }
            }

            var roofRoom = new RoomDefinition(1003);
            var leftWing = new RoomDefinition(1001);
            var rightWing = new RoomDefinition(1002);
            grid[topConnectIndex]!.SetConnection(Up, roofRoom);
            grid[leftWingConnectIndex]!.SetConnection(South, leftWing);
            grid[rightWingConnectIndex]!.SetConnection(South, rightWing);
            roofRoom.Claimed = true;
            leftWing.Claimed = true;
            rightWing.Claimed = true;
            this.sourceRoom.IsSource = true;

            // The core room takes 2x2x2 grid rooms.
            this.coreRoom = grid[GetRoomIndex(random.NextInt(4), 0, 2)]!;
            var core = this.coreRoom;
            core.Claimed = true;
            core.Connections[East]!.Claimed = true;
            core.Connections[North]!.Claimed = true;
            core.Connections[East]!.Connections[North]!.Claimed = true;
            core.Connections[Up]!.Claimed = true;
            core.Connections[East]!.Connections[Up]!.Claimed = true;
            core.Connections[North]!.Connections[Up]!.Claimed = true;
            core.Connections[East]!.Connections[North]!.Connections[Up]!.Claimed = true;

            var rooms = new List<RoomDefinition>();
            foreach (var room in grid)
            {
                if (room is null)
                    continue;

                room.UpdateOpenings();
                rooms.Add(room);
            }

            roofRoom.UpdateOpenings();
            FeatureHelpers.Shuffle(rooms, random);

            var scanIndex = 1;
            foreach (var room in rooms)
            {
                var closed = 0;
                for (var attempt = 0; closed < 2 && attempt < 5; attempt++)
                {
                    var direction = random.NextInt(6);
                    if (!room.HasOpening[direction])
                        continue;

                    var opposite = Opposite(direction);
                    var neighbor = room.Connections[direction]!;
                    room.HasOpening[direction] = false;
                    neighbor.HasOpening[opposite] = false;
                    if (room.FindSource(scanIndex++) && neighbor.FindSource(scanIndex++))
                    {
                        closed++;
                    }
                    else
                    {
                        room.HasOpening[direction] = true;
                        neighbor.HasOpening[opposite] = true;
                    }
                }
            }

            rooms.Add(roofRoom);
            rooms.Add(leftWing);
            rooms.Add(rightWing);
            return rooms;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var waterHeight = Math.Max(context.Level.SeaLevel, 64) - this.BoundingBox.MinY;
            this.GenerateWaterBox(context, 0, 0, 0, 58, waterHeight, 58);
            this.GenerateWing(context, false, 0);
            this.GenerateWing(context, true, 33);
            this.GenerateEntranceArches(context);
            this.GenerateEntranceWall(context);
            this.GenerateRoofPiece(context);
            this.GenerateLowerWall(context);
            this.GenerateMiddleWall(context);
            this.GenerateUpperWall(context);

            // The pillars along the outer edge and both sides of the entrance.
            for (var pillarX = 0; pillarX < 7; pillarX++)
            {
                var pillarZ = 0;
                while (pillarZ < 7)
                {
                    if (pillarZ == 0 && pillarX == 3)
                        pillarZ = 6;

                    var x = pillarX * 9;
                    var z = pillarZ * 9;
                    for (var w = 0; w < 4; w++)
                    {
                        for (var d = 0; d < 4; d++)
                        {
                            this.Place(context, baseLight, x + w, 0, z + d);
                            this.FillColumnDown(context.Level, baseLight, x + w, -1, z + d, context.Box);
                        }
                    }

                    pillarZ += pillarX is 0 or 6 ? 1 : 6;
                }
            }

            // Water steps around the base.
            for (var i = 0; i < 5; i++)
            {
                this.GenerateWaterBox(context, -1 - i, i * 2, -1 - i, -1 - i, 23, 58 + i);
                this.GenerateWaterBox(context, 58 + i, i * 2, -1 - i, 58 + i, 23, 58 + i);
                this.GenerateWaterBox(context, -i, i * 2, -1 - i, 57 + i, 23, -1 - i);
                this.GenerateWaterBox(context, -i, i * 2, 58 + i, 57 + i, 23, 58 + i);
            }

            foreach (var child in this.childPieces)
            {
                if (child.BoundingBox.Intersects(context.Box))
                    child.PostProcess(context);
            }
        }

        private void GenerateWing(StructurePieceContext context, bool isFlipped, int xOffset)
        {
            if (!this.ChunkIntersects(context, xOffset, 0, xOffset + 23, 20))
                return;

            this.Fill(context, xOffset, 0, 0, xOffset + 24, 0, 20, baseGray);
            this.GenerateWaterBox(context, xOffset, 1, 0, xOffset + 24, 10, 20);

            for (var i = 0; i < 4; i++)
            {
                this.Fill(context, xOffset + i, i + 1, i, xOffset + i, i + 1, 20, baseLight);
                this.Fill(context, xOffset + i + 7, i + 5, i + 7, xOffset + i + 7, i + 5, 20, baseLight);
                this.Fill(context, xOffset + 17 - i, i + 5, i + 7, xOffset + 17 - i, i + 5, 20, baseLight);
                this.Fill(context, xOffset + 24 - i, i + 1, i, xOffset + 24 - i, i + 1, 20, baseLight);
                this.Fill(context, xOffset + i + 1, i + 1, i, xOffset + 23 - i, i + 1, i, baseLight);
                this.Fill(context, xOffset + i + 8, i + 5, i + 7, xOffset + 16 - i, i + 5, i + 7, baseLight);
            }

            this.Fill(context, xOffset + 4, 4, 4, xOffset + 6, 4, 20, baseGray);
            this.Fill(context, xOffset + 7, 4, 4, xOffset + 17, 4, 6, baseGray);
            this.Fill(context, xOffset + 18, 4, 4, xOffset + 20, 4, 20, baseGray);
            this.Fill(context, xOffset + 11, 8, 11, xOffset + 13, 8, 20, baseGray);
            this.Place(context, baseLight, xOffset + 12, 9, 12);
            this.Place(context, baseLight, xOffset + 12, 9, 15);
            this.Place(context, baseLight, xOffset + 12, 9, 18);

            var left = xOffset + (isFlipped ? 19 : 5);
            var right = xOffset + (isFlipped ? 5 : 19);
            for (var z = 20; z >= 5; z -= 3)
                this.Place(context, baseLight, left, 5, z);

            for (var z = 19; z >= 7; z -= 3)
                this.Place(context, baseLight, right, 5, z);

            for (var i = 0; i < 4; i++)
                this.Place(context, baseLight, isFlipped ? xOffset + 24 - (17 - i * 3) : xOffset + 17 - i * 3, 5, 5);

            this.Place(context, baseLight, right, 5, 5);
            this.Fill(context, xOffset + 11, 1, 12, xOffset + 13, 7, 12, baseGray);
            this.Fill(context, xOffset + 12, 1, 11, xOffset + 12, 7, 13, baseGray);
        }

        private void GenerateEntranceArches(StructurePieceContext context)
        {
            if (!this.ChunkIntersects(context, 22, 5, 35, 17))
                return;

            this.GenerateWaterBox(context, 25, 0, 0, 32, 8, 20);

            for (var i = 0; i < 4; i++)
            {
                var z = 5 + i * 4;
                this.Fill(context, 24, 2, z, 24, 4, z, baseLight);
                this.Fill(context, 22, 4, z, 23, 4, z, baseLight);
                this.Place(context, baseLight, 25, 5, z);
                this.Place(context, baseLight, 26, 6, z);
                this.Place(context, lamp, 26, 5, z);
                this.Fill(context, 33, 2, z, 33, 4, z, baseLight);
                this.Fill(context, 34, 4, z, 35, 4, z, baseLight);
                this.Place(context, baseLight, 32, 5, z);
                this.Place(context, baseLight, 31, 6, z);
                this.Place(context, lamp, 31, 5, z);
                this.Fill(context, 27, 6, z, 30, 6, z, baseGray);
            }
        }

        private void GenerateEntranceWall(StructurePieceContext context)
        {
            if (!this.ChunkIntersects(context, 15, 20, 42, 21))
                return;

            this.Fill(context, 15, 0, 21, 42, 0, 21, baseGray);
            this.GenerateWaterBox(context, 26, 1, 21, 31, 3, 21);
            this.Fill(context, 21, 12, 21, 36, 12, 21, baseGray);
            this.Fill(context, 17, 11, 21, 40, 11, 21, baseGray);
            this.Fill(context, 16, 10, 21, 41, 10, 21, baseGray);
            this.Fill(context, 15, 7, 21, 42, 9, 21, baseGray);
            this.Fill(context, 16, 6, 21, 41, 6, 21, baseGray);
            this.Fill(context, 17, 5, 21, 40, 5, 21, baseGray);
            this.Fill(context, 21, 4, 21, 36, 4, 21, baseGray);
            this.Fill(context, 22, 3, 21, 26, 3, 21, baseGray);
            this.Fill(context, 31, 3, 21, 35, 3, 21, baseGray);
            this.Fill(context, 23, 2, 21, 25, 2, 21, baseGray);
            this.Fill(context, 32, 2, 21, 34, 2, 21, baseGray);
            this.Fill(context, 28, 4, 20, 29, 4, 21, baseLight);
            this.Place(context, baseLight, 27, 3, 21);
            this.Place(context, baseLight, 30, 3, 21);
            this.Place(context, baseLight, 26, 2, 21);
            this.Place(context, baseLight, 31, 2, 21);
            this.Place(context, baseLight, 25, 1, 21);
            this.Place(context, baseLight, 32, 1, 21);

            for (var i = 0; i < 7; i++)
            {
                this.Place(context, baseBlack, 28 - i, 6 + i, 21);
                this.Place(context, baseBlack, 29 + i, 6 + i, 21);
            }

            for (var i = 0; i < 4; i++)
            {
                this.Place(context, baseBlack, 28 - i, 9 + i, 21);
                this.Place(context, baseBlack, 29 + i, 9 + i, 21);
            }

            this.Place(context, baseBlack, 28, 12, 21);
            this.Place(context, baseBlack, 29, 12, 21);

            for (var i = 0; i < 3; i++)
            {
                this.Place(context, baseBlack, 22 - i * 2, 8, 21);
                this.Place(context, baseBlack, 22 - i * 2, 9, 21);
                this.Place(context, baseBlack, 35 + i * 2, 8, 21);
                this.Place(context, baseBlack, 35 + i * 2, 9, 21);
            }

            this.GenerateWaterBox(context, 15, 13, 21, 42, 15, 21);
            this.GenerateWaterBox(context, 15, 1, 21, 15, 6, 21);
            this.GenerateWaterBox(context, 16, 1, 21, 16, 5, 21);
            this.GenerateWaterBox(context, 17, 1, 21, 20, 4, 21);
            this.GenerateWaterBox(context, 21, 1, 21, 21, 3, 21);
            this.GenerateWaterBox(context, 22, 1, 21, 22, 2, 21);
            this.GenerateWaterBox(context, 23, 1, 21, 24, 1, 21);
            this.GenerateWaterBox(context, 42, 1, 21, 42, 6, 21);
            this.GenerateWaterBox(context, 41, 1, 21, 41, 5, 21);
            this.GenerateWaterBox(context, 37, 1, 21, 40, 4, 21);
            this.GenerateWaterBox(context, 36, 1, 21, 36, 3, 21);
            this.GenerateWaterBox(context, 33, 1, 21, 34, 1, 21);
            this.GenerateWaterBox(context, 35, 1, 21, 35, 2, 21);
        }

        private void GenerateRoofPiece(StructurePieceContext context)
        {
            if (!this.ChunkIntersects(context, 21, 21, 36, 36))
                return;

            this.Fill(context, 21, 0, 22, 36, 0, 36, baseGray);
            this.GenerateWaterBox(context, 21, 1, 22, 36, 23, 36);

            for (var i = 0; i < 4; i++)
            {
                this.Fill(context, 21 + i, 13 + i, 21 + i, 36 - i, 13 + i, 21 + i, baseLight);
                this.Fill(context, 21 + i, 13 + i, 36 - i, 36 - i, 13 + i, 36 - i, baseLight);
                this.Fill(context, 21 + i, 13 + i, 22 + i, 21 + i, 13 + i, 35 - i, baseLight);
                this.Fill(context, 36 - i, 13 + i, 22 + i, 36 - i, 13 + i, 35 - i, baseLight);
            }

            this.Fill(context, 25, 16, 25, 32, 16, 32, baseGray);
            this.Fill(context, 25, 17, 25, 25, 19, 25, baseLight);
            this.Fill(context, 32, 17, 25, 32, 19, 25, baseLight);
            this.Fill(context, 25, 17, 32, 25, 19, 32, baseLight);
            this.Fill(context, 32, 17, 32, 32, 19, 32, baseLight);
            this.Place(context, baseLight, 26, 20, 26);
            this.Place(context, baseLight, 27, 21, 27);
            this.Place(context, lamp, 27, 20, 27);
            this.Place(context, baseLight, 26, 20, 31);
            this.Place(context, baseLight, 27, 21, 30);
            this.Place(context, lamp, 27, 20, 30);
            this.Place(context, baseLight, 31, 20, 31);
            this.Place(context, baseLight, 30, 21, 30);
            this.Place(context, lamp, 30, 20, 30);
            this.Place(context, baseLight, 31, 20, 26);
            this.Place(context, baseLight, 30, 21, 27);
            this.Place(context, lamp, 30, 20, 27);
            this.Fill(context, 28, 21, 27, 29, 21, 27, baseGray);
            this.Fill(context, 27, 21, 28, 27, 21, 29, baseGray);
            this.Fill(context, 28, 21, 30, 29, 21, 30, baseGray);
            this.Fill(context, 30, 21, 28, 30, 21, 29, baseGray);
        }

        private void GenerateLowerWall(StructurePieceContext context)
        {
            if (this.ChunkIntersects(context, 0, 21, 6, 58))
            {
                this.Fill(context, 0, 0, 21, 6, 0, 57, baseGray);
                this.GenerateWaterBox(context, 0, 1, 21, 6, 7, 57);
                this.Fill(context, 4, 4, 21, 6, 4, 53, baseGray);

                for (var i = 0; i < 4; i++)
                    this.Fill(context, i, i + 1, 21, i, i + 1, 57 - i, baseLight);

                for (var z = 23; z < 53; z += 3)
                    this.Place(context, baseLight, 5, 5, z);

                this.Place(context, baseLight, 5, 5, 52);

                // Vanilla places these steps a second time.
                for (var i = 0; i < 4; i++)
                    this.Fill(context, i, i + 1, 21, i, i + 1, 57 - i, baseLight);

                this.Fill(context, 4, 1, 52, 6, 3, 52, baseGray);
                this.Fill(context, 5, 1, 51, 5, 3, 53, baseGray);
            }

            if (this.ChunkIntersects(context, 51, 21, 58, 58))
            {
                this.Fill(context, 51, 0, 21, 57, 0, 57, baseGray);
                this.GenerateWaterBox(context, 51, 1, 21, 57, 7, 57);
                this.Fill(context, 51, 4, 21, 53, 4, 53, baseGray);

                for (var i = 0; i < 4; i++)
                    this.Fill(context, 57 - i, i + 1, 21, 57 - i, i + 1, 57 - i, baseLight);

                for (var z = 23; z < 53; z += 3)
                    this.Place(context, baseLight, 52, 5, z);

                this.Place(context, baseLight, 52, 5, 52);
                this.Fill(context, 51, 1, 52, 53, 3, 52, baseGray);
                this.Fill(context, 52, 1, 51, 52, 3, 53, baseGray);
            }

            if (this.ChunkIntersects(context, 0, 51, 57, 57))
            {
                this.Fill(context, 7, 0, 51, 50, 0, 57, baseGray);
                this.GenerateWaterBox(context, 7, 1, 51, 50, 10, 57);

                for (var i = 0; i < 4; i++)
                    this.Fill(context, i + 1, i + 1, 57 - i, 56 - i, i + 1, 57 - i, baseLight);
            }
        }

        private void GenerateMiddleWall(StructurePieceContext context)
        {
            if (this.ChunkIntersects(context, 7, 21, 13, 50))
            {
                this.Fill(context, 7, 0, 21, 13, 0, 50, baseGray);
                this.GenerateWaterBox(context, 7, 1, 21, 13, 10, 50);
                this.Fill(context, 11, 8, 21, 13, 8, 53, baseGray);

                for (var i = 0; i < 4; i++)
                    this.Fill(context, i + 7, i + 5, 21, i + 7, i + 5, 54, baseLight);

                for (var z = 21; z <= 45; z += 3)
                    this.Place(context, baseLight, 12, 9, z);
            }

            if (this.ChunkIntersects(context, 44, 21, 50, 54))
            {
                this.Fill(context, 44, 0, 21, 50, 0, 50, baseGray);
                this.GenerateWaterBox(context, 44, 1, 21, 50, 10, 50);
                this.Fill(context, 44, 8, 21, 46, 8, 53, baseGray);

                for (var i = 0; i < 4; i++)
                    this.Fill(context, 50 - i, i + 5, 21, 50 - i, i + 5, 54, baseLight);

                for (var z = 21; z <= 45; z += 3)
                    this.Place(context, baseLight, 45, 9, z);
            }

            if (this.ChunkIntersects(context, 8, 44, 49, 54))
            {
                this.Fill(context, 14, 0, 44, 43, 0, 50, baseGray);
                this.GenerateWaterBox(context, 14, 1, 44, 43, 10, 50);

                for (var x = 12; x <= 45; x += 3)
                {
                    this.Place(context, baseLight, x, 9, 45);
                    this.Place(context, baseLight, x, 9, 52);
                    if (x is 12 or 18 or 24 or 33 or 39 or 45)
                    {
                        this.Place(context, baseLight, x, 9, 47);
                        this.Place(context, baseLight, x, 9, 50);
                        this.Place(context, baseLight, x, 10, 45);
                        this.Place(context, baseLight, x, 10, 46);
                        this.Place(context, baseLight, x, 10, 51);
                        this.Place(context, baseLight, x, 10, 52);
                        this.Place(context, baseLight, x, 11, 47);
                        this.Place(context, baseLight, x, 11, 50);
                        this.Place(context, baseLight, x, 12, 48);
                        this.Place(context, baseLight, x, 12, 49);
                    }
                }

                for (var i = 0; i < 3; i++)
                    this.Fill(context, 8 + i, 5 + i, 54, 49 - i, 5 + i, 54, baseGray);

                this.Fill(context, 11, 8, 54, 46, 8, 54, baseLight);
                this.Fill(context, 14, 8, 44, 43, 8, 53, baseGray);
            }
        }

        private void GenerateUpperWall(StructurePieceContext context)
        {
            if (this.ChunkIntersects(context, 14, 21, 20, 43))
            {
                this.Fill(context, 14, 0, 21, 20, 0, 43, baseGray);
                this.GenerateWaterBox(context, 14, 1, 22, 20, 14, 43);
                this.Fill(context, 18, 12, 22, 20, 12, 39, baseGray);
                this.Fill(context, 18, 12, 21, 20, 12, 21, baseLight);

                for (var i = 0; i < 4; i++)
                    this.Fill(context, i + 14, i + 9, 21, i + 14, i + 9, 43 - i, baseLight);

                for (var z = 23; z <= 39; z += 3)
                    this.Place(context, baseLight, 19, 13, z);
            }

            if (this.ChunkIntersects(context, 37, 21, 43, 43))
            {
                this.Fill(context, 37, 0, 21, 43, 0, 43, baseGray);
                this.GenerateWaterBox(context, 37, 1, 22, 43, 14, 43);
                this.Fill(context, 37, 12, 22, 39, 12, 39, baseGray);
                this.Fill(context, 37, 12, 21, 39, 12, 21, baseLight);

                for (var i = 0; i < 4; i++)
                    this.Fill(context, 43 - i, i + 9, 21, 43 - i, i + 9, 43 - i, baseLight);

                for (var z = 23; z <= 39; z += 3)
                    this.Place(context, baseLight, 38, 13, z);
            }

            if (this.ChunkIntersects(context, 15, 37, 42, 43))
            {
                this.Fill(context, 21, 0, 37, 36, 0, 43, baseGray);
                this.GenerateWaterBox(context, 21, 1, 37, 36, 14, 43);
                this.Fill(context, 21, 12, 37, 36, 12, 39, baseGray);

                for (var i = 0; i < 4; i++)
                    this.Fill(context, 15 + i, i + 9, 43 - i, 42 - i, i + 9, 43 - i, baseLight);

                for (var x = 21; x <= 36; x += 3)
                    this.Place(context, baseLight, x, 13, 38);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentCoreRoom</c>: the 2x2x2 treasure room with its gold blocks.
    /// </summary>
    public sealed class OceanMonumentCoreRoom : OceanMonumentPiece
    {
        internal OceanMonumentCoreRoom(BlockFace orientation, RoomDefinition room) : base(1, orientation, room, 2, 2, 2)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.GenerateBoxOnFillOnly(context, 1, 8, 0, 14, 8, 14, baseGray);
            this.Fill(context, 0, 7, 0, 0, 7, 15, baseLight);
            this.Fill(context, 15, 7, 0, 15, 7, 15, baseLight);
            this.Fill(context, 1, 7, 0, 15, 7, 0, baseLight);
            this.Fill(context, 1, 7, 15, 14, 7, 15, baseLight);

            for (var y = 1; y <= 6; y++)
            {
                var block = y is 2 or 6 ? baseGray : baseLight;
                for (var x = 0; x <= 15; x += 15)
                {
                    this.Fill(context, x, y, 0, x, y, 1, block);
                    this.Fill(context, x, y, 6, x, y, 9, block);
                    this.Fill(context, x, y, 14, x, y, 15, block);
                }

                this.Fill(context, 1, y, 0, 1, y, 0, block);
                this.Fill(context, 6, y, 0, 9, y, 0, block);
                this.Fill(context, 14, y, 0, 14, y, 0, block);
                this.Fill(context, 1, y, 15, 14, y, 15, block);
            }

            this.Fill(context, 6, 3, 6, 9, 6, 9, baseBlack);
            this.Fill(context, 7, 4, 7, 8, 5, 8, BlocksRegistry.Get(Material.GoldBlock));

            for (var y = 3; y <= 6; y += 3)
            {
                for (var x = 6; x <= 9; x += 3)
                {
                    this.Place(context, lamp, x, y, 6);
                    this.Place(context, lamp, x, y, 9);
                }
            }

            this.Fill(context, 5, 1, 6, 5, 2, 6, baseLight);
            this.Fill(context, 5, 1, 9, 5, 2, 9, baseLight);
            this.Fill(context, 10, 1, 6, 10, 2, 6, baseLight);
            this.Fill(context, 10, 1, 9, 10, 2, 9, baseLight);
            this.Fill(context, 6, 1, 5, 6, 2, 5, baseLight);
            this.Fill(context, 9, 1, 5, 9, 2, 5, baseLight);
            this.Fill(context, 6, 1, 10, 6, 2, 10, baseLight);
            this.Fill(context, 9, 1, 10, 9, 2, 10, baseLight);
            this.Fill(context, 5, 2, 5, 5, 6, 5, baseLight);
            this.Fill(context, 5, 2, 10, 5, 6, 10, baseLight);
            this.Fill(context, 10, 2, 5, 10, 6, 5, baseLight);
            this.Fill(context, 10, 2, 10, 10, 6, 10, baseLight);
            this.Fill(context, 5, 7, 1, 5, 7, 6, baseLight);
            this.Fill(context, 10, 7, 1, 10, 7, 6, baseLight);
            this.Fill(context, 5, 7, 9, 5, 7, 14, baseLight);
            this.Fill(context, 10, 7, 9, 10, 7, 14, baseLight);
            this.Fill(context, 1, 7, 5, 6, 7, 5, baseLight);
            this.Fill(context, 1, 7, 10, 6, 7, 10, baseLight);
            this.Fill(context, 9, 7, 5, 14, 7, 5, baseLight);
            this.Fill(context, 9, 7, 10, 14, 7, 10, baseLight);
            this.Fill(context, 2, 1, 2, 2, 1, 3, baseLight);
            this.Fill(context, 3, 1, 2, 3, 1, 2, baseLight);
            this.Fill(context, 13, 1, 2, 13, 1, 3, baseLight);
            this.Fill(context, 12, 1, 2, 12, 1, 2, baseLight);
            this.Fill(context, 2, 1, 12, 2, 1, 13, baseLight);
            this.Fill(context, 3, 1, 13, 3, 1, 13, baseLight);
            this.Fill(context, 13, 1, 12, 13, 1, 13, baseLight);
            this.Fill(context, 12, 1, 13, 12, 1, 13, baseLight);
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentDoubleXRoom</c>: two grid rooms side by side.
    /// </summary>
    public sealed class OceanMonumentDoubleXRoom : OceanMonumentPiece
    {
        internal OceanMonumentDoubleXRoom(BlockFace orientation, RoomDefinition room) : base(1, orientation, room, 2, 1, 1)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var east = this.Room.Connections[East]!;
            var west = this.Room;
            if (this.Room.Index / 25 > 0)
            {
                this.GenerateDefaultFloor(context, 8, 0, east.HasOpening[Down]);
                this.GenerateDefaultFloor(context, 0, 0, west.HasOpening[Down]);
            }

            if (west.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 4, 1, 7, 4, 6, baseGray);

            if (east.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 8, 4, 1, 14, 4, 6, baseGray);

            this.Fill(context, 0, 3, 0, 0, 3, 7, baseLight);
            this.Fill(context, 15, 3, 0, 15, 3, 7, baseLight);
            this.Fill(context, 1, 3, 0, 15, 3, 0, baseLight);
            this.Fill(context, 1, 3, 7, 14, 3, 7, baseLight);
            this.Fill(context, 0, 2, 0, 0, 2, 7, baseGray);
            this.Fill(context, 15, 2, 0, 15, 2, 7, baseGray);
            this.Fill(context, 1, 2, 0, 15, 2, 0, baseGray);
            this.Fill(context, 1, 2, 7, 14, 2, 7, baseGray);
            this.Fill(context, 0, 1, 0, 0, 1, 7, baseLight);
            this.Fill(context, 15, 1, 0, 15, 1, 7, baseLight);
            this.Fill(context, 1, 1, 0, 15, 1, 0, baseLight);
            this.Fill(context, 1, 1, 7, 14, 1, 7, baseLight);
            this.Fill(context, 5, 1, 0, 10, 1, 4, baseLight);
            this.Fill(context, 6, 2, 0, 9, 2, 3, baseGray);
            this.Fill(context, 5, 3, 0, 10, 3, 4, baseLight);
            this.Place(context, lamp, 6, 2, 3);
            this.Place(context, lamp, 9, 2, 3);

            if (west.HasOpening[South])
                this.GenerateWaterBox(context, 3, 1, 0, 4, 2, 0);

            if (west.HasOpening[North])
                this.GenerateWaterBox(context, 3, 1, 7, 4, 2, 7);

            if (west.HasOpening[West])
                this.GenerateWaterBox(context, 0, 1, 3, 0, 2, 4);

            if (east.HasOpening[South])
                this.GenerateWaterBox(context, 11, 1, 0, 12, 2, 0);

            if (east.HasOpening[North])
                this.GenerateWaterBox(context, 11, 1, 7, 12, 2, 7);

            if (east.HasOpening[East])
                this.GenerateWaterBox(context, 15, 1, 3, 15, 2, 4);
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentDoubleXYRoom</c>: four grid rooms, two wide and two high.
    /// </summary>
    public sealed class OceanMonumentDoubleXYRoom : OceanMonumentPiece
    {
        internal OceanMonumentDoubleXYRoom(BlockFace orientation, RoomDefinition room) : base(1, orientation, room, 2, 2, 1)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var east = this.Room.Connections[East]!;
            var west = this.Room;
            var westUp = west.Connections[Up]!;
            var eastUp = east.Connections[Up]!;
            if (this.Room.Index / 25 > 0)
            {
                this.GenerateDefaultFloor(context, 8, 0, east.HasOpening[Down]);
                this.GenerateDefaultFloor(context, 0, 0, west.HasOpening[Down]);
            }

            if (westUp.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 8, 1, 7, 8, 6, baseGray);

            if (eastUp.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 8, 8, 1, 14, 8, 6, baseGray);

            for (var y = 1; y <= 7; y++)
            {
                var block = y is 2 or 6 ? baseGray : baseLight;
                this.Fill(context, 0, y, 0, 0, y, 7, block);
                this.Fill(context, 15, y, 0, 15, y, 7, block);
                this.Fill(context, 1, y, 0, 15, y, 0, block);
                this.Fill(context, 1, y, 7, 14, y, 7, block);
            }

            this.Fill(context, 2, 1, 3, 2, 7, 4, baseLight);
            this.Fill(context, 3, 1, 2, 4, 7, 2, baseLight);
            this.Fill(context, 3, 1, 5, 4, 7, 5, baseLight);
            this.Fill(context, 13, 1, 3, 13, 7, 4, baseLight);
            this.Fill(context, 11, 1, 2, 12, 7, 2, baseLight);
            this.Fill(context, 11, 1, 5, 12, 7, 5, baseLight);
            this.Fill(context, 5, 1, 3, 5, 3, 4, baseLight);
            this.Fill(context, 10, 1, 3, 10, 3, 4, baseLight);
            this.Fill(context, 5, 7, 2, 10, 7, 5, baseLight);
            this.Fill(context, 5, 5, 2, 5, 7, 2, baseLight);
            this.Fill(context, 10, 5, 2, 10, 7, 2, baseLight);
            this.Fill(context, 5, 5, 5, 5, 7, 5, baseLight);
            this.Fill(context, 10, 5, 5, 10, 7, 5, baseLight);
            this.Place(context, baseLight, 6, 6, 2);
            this.Place(context, baseLight, 9, 6, 2);
            this.Place(context, baseLight, 6, 6, 5);
            this.Place(context, baseLight, 9, 6, 5);
            this.Fill(context, 5, 4, 3, 6, 4, 4, baseLight);
            this.Fill(context, 9, 4, 3, 10, 4, 4, baseLight);
            this.Place(context, lamp, 5, 4, 2);
            this.Place(context, lamp, 5, 4, 5);
            this.Place(context, lamp, 10, 4, 2);
            this.Place(context, lamp, 10, 4, 5);

            if (west.HasOpening[South])
                this.GenerateWaterBox(context, 3, 1, 0, 4, 2, 0);

            if (west.HasOpening[North])
                this.GenerateWaterBox(context, 3, 1, 7, 4, 2, 7);

            if (west.HasOpening[West])
                this.GenerateWaterBox(context, 0, 1, 3, 0, 2, 4);

            if (east.HasOpening[South])
                this.GenerateWaterBox(context, 11, 1, 0, 12, 2, 0);

            if (east.HasOpening[North])
                this.GenerateWaterBox(context, 11, 1, 7, 12, 2, 7);

            if (east.HasOpening[East])
                this.GenerateWaterBox(context, 15, 1, 3, 15, 2, 4);

            if (westUp.HasOpening[South])
                this.GenerateWaterBox(context, 3, 5, 0, 4, 6, 0);

            if (westUp.HasOpening[North])
                this.GenerateWaterBox(context, 3, 5, 7, 4, 6, 7);

            if (westUp.HasOpening[West])
                this.GenerateWaterBox(context, 0, 5, 3, 0, 6, 4);

            if (eastUp.HasOpening[South])
                this.GenerateWaterBox(context, 11, 5, 0, 12, 6, 0);

            if (eastUp.HasOpening[North])
                this.GenerateWaterBox(context, 11, 5, 7, 12, 6, 7);

            if (eastUp.HasOpening[East])
                this.GenerateWaterBox(context, 15, 5, 3, 15, 6, 4);
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentDoubleYRoom</c>: two grid rooms stacked.
    /// </summary>
    public sealed class OceanMonumentDoubleYRoom : OceanMonumentPiece
    {
        internal OceanMonumentDoubleYRoom(BlockFace orientation, RoomDefinition room) : base(1, orientation, room, 1, 2, 1)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            if (this.Room.Index / 25 > 0)
                this.GenerateDefaultFloor(context, 0, 0, this.Room.HasOpening[Down]);

            var above = this.Room.Connections[Up]!;
            if (above.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 8, 1, 6, 8, 6, baseGray);

            this.Fill(context, 0, 4, 0, 0, 4, 7, baseLight);
            this.Fill(context, 7, 4, 0, 7, 4, 7, baseLight);
            this.Fill(context, 1, 4, 0, 6, 4, 0, baseLight);
            this.Fill(context, 1, 4, 7, 6, 4, 7, baseLight);
            this.Fill(context, 2, 4, 1, 2, 4, 2, baseLight);
            this.Fill(context, 1, 4, 2, 1, 4, 2, baseLight);
            this.Fill(context, 5, 4, 1, 5, 4, 2, baseLight);
            this.Fill(context, 6, 4, 2, 6, 4, 2, baseLight);
            this.Fill(context, 2, 4, 5, 2, 4, 6, baseLight);
            this.Fill(context, 1, 4, 5, 1, 4, 5, baseLight);
            this.Fill(context, 5, 4, 5, 5, 4, 6, baseLight);
            this.Fill(context, 6, 4, 5, 6, 4, 5, baseLight);

            // The lower room's walls, then the upper room's.
            var room = this.Room;
            for (var y = 1; y <= 5; y += 4)
            {
                if (room.HasOpening[South])
                {
                    this.Fill(context, 2, y, 0, 2, y + 2, 0, baseLight);
                    this.Fill(context, 5, y, 0, 5, y + 2, 0, baseLight);
                    this.Fill(context, 3, y + 2, 0, 4, y + 2, 0, baseLight);
                }
                else
                {
                    this.Fill(context, 0, y, 0, 7, y + 2, 0, baseLight);
                    this.Fill(context, 0, y + 1, 0, 7, y + 1, 0, baseGray);
                }

                if (room.HasOpening[North])
                {
                    this.Fill(context, 2, y, 7, 2, y + 2, 7, baseLight);
                    this.Fill(context, 5, y, 7, 5, y + 2, 7, baseLight);
                    this.Fill(context, 3, y + 2, 7, 4, y + 2, 7, baseLight);
                }
                else
                {
                    this.Fill(context, 0, y, 7, 7, y + 2, 7, baseLight);
                    this.Fill(context, 0, y + 1, 7, 7, y + 1, 7, baseGray);
                }

                if (room.HasOpening[West])
                {
                    this.Fill(context, 0, y, 2, 0, y + 2, 2, baseLight);
                    this.Fill(context, 0, y, 5, 0, y + 2, 5, baseLight);
                    this.Fill(context, 0, y + 2, 3, 0, y + 2, 4, baseLight);
                }
                else
                {
                    this.Fill(context, 0, y, 0, 0, y + 2, 7, baseLight);
                    this.Fill(context, 0, y + 1, 0, 0, y + 1, 7, baseGray);
                }

                if (room.HasOpening[East])
                {
                    this.Fill(context, 7, y, 2, 7, y + 2, 2, baseLight);
                    this.Fill(context, 7, y, 5, 7, y + 2, 5, baseLight);
                    this.Fill(context, 7, y + 2, 3, 7, y + 2, 4, baseLight);
                }
                else
                {
                    this.Fill(context, 7, y, 0, 7, y + 2, 7, baseLight);
                    this.Fill(context, 7, y + 1, 0, 7, y + 1, 7, baseGray);
                }

                room = above;
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentDoubleYZRoom</c>: four grid rooms, two deep and two high.
    /// </summary>
    public sealed class OceanMonumentDoubleYZRoom : OceanMonumentPiece
    {
        internal OceanMonumentDoubleYZRoom(BlockFace orientation, RoomDefinition room) : base(1, orientation, room, 1, 2, 2)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var north = this.Room.Connections[North]!;
            var south = this.Room;
            var northUp = north.Connections[Up]!;
            var southUp = south.Connections[Up]!;
            if (this.Room.Index / 25 > 0)
            {
                this.GenerateDefaultFloor(context, 0, 8, north.HasOpening[Down]);
                this.GenerateDefaultFloor(context, 0, 0, south.HasOpening[Down]);
            }

            if (southUp.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 8, 1, 6, 8, 7, baseGray);

            if (northUp.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 8, 8, 6, 8, 14, baseGray);

            for (var y = 1; y <= 7; y++)
            {
                var block = y is 2 or 6 ? baseGray : baseLight;
                this.Fill(context, 0, y, 0, 0, y, 15, block);
                this.Fill(context, 7, y, 0, 7, y, 15, block);
                this.Fill(context, 1, y, 0, 6, y, 0, block);
                this.Fill(context, 1, y, 15, 6, y, 15, block);
            }

            for (var y = 1; y <= 7; y++)
                this.Fill(context, 3, y, 7, 4, y, 8, y is 2 or 6 ? lamp : baseBlack);

            if (south.HasOpening[South])
                this.GenerateWaterBox(context, 3, 1, 0, 4, 2, 0);

            if (south.HasOpening[East])
                this.GenerateWaterBox(context, 7, 1, 3, 7, 2, 4);

            if (south.HasOpening[West])
                this.GenerateWaterBox(context, 0, 1, 3, 0, 2, 4);

            if (north.HasOpening[North])
                this.GenerateWaterBox(context, 3, 1, 15, 4, 2, 15);

            if (north.HasOpening[West])
                this.GenerateWaterBox(context, 0, 1, 11, 0, 2, 12);

            if (north.HasOpening[East])
                this.GenerateWaterBox(context, 7, 1, 11, 7, 2, 12);

            if (southUp.HasOpening[South])
                this.GenerateWaterBox(context, 3, 5, 0, 4, 6, 0);

            if (southUp.HasOpening[East])
            {
                this.GenerateWaterBox(context, 7, 5, 3, 7, 6, 4);
                this.Fill(context, 5, 4, 2, 6, 4, 5, baseLight);
                this.Fill(context, 6, 1, 2, 6, 3, 2, baseLight);
                this.Fill(context, 6, 1, 5, 6, 3, 5, baseLight);
            }

            if (southUp.HasOpening[West])
            {
                this.GenerateWaterBox(context, 0, 5, 3, 0, 6, 4);
                this.Fill(context, 1, 4, 2, 2, 4, 5, baseLight);
                this.Fill(context, 1, 1, 2, 1, 3, 2, baseLight);
                this.Fill(context, 1, 1, 5, 1, 3, 5, baseLight);
            }

            if (northUp.HasOpening[North])
                this.GenerateWaterBox(context, 3, 5, 15, 4, 6, 15);

            if (northUp.HasOpening[West])
            {
                this.GenerateWaterBox(context, 0, 5, 11, 0, 6, 12);
                this.Fill(context, 1, 4, 10, 2, 4, 13, baseLight);
                this.Fill(context, 1, 1, 10, 1, 3, 10, baseLight);
                this.Fill(context, 1, 1, 13, 1, 3, 13, baseLight);
            }

            if (northUp.HasOpening[East])
            {
                this.GenerateWaterBox(context, 7, 5, 11, 7, 6, 12);
                this.Fill(context, 5, 4, 10, 6, 4, 13, baseLight);
                this.Fill(context, 6, 1, 10, 6, 3, 10, baseLight);
                this.Fill(context, 6, 1, 13, 6, 3, 13, baseLight);
            }
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentDoubleZRoom</c>: two grid rooms one behind the other.
    /// </summary>
    public sealed class OceanMonumentDoubleZRoom : OceanMonumentPiece
    {
        internal OceanMonumentDoubleZRoom(BlockFace orientation, RoomDefinition room) : base(1, orientation, room, 1, 1, 2)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var north = this.Room.Connections[North]!;
            var south = this.Room;
            if (this.Room.Index / 25 > 0)
            {
                this.GenerateDefaultFloor(context, 0, 8, north.HasOpening[Down]);
                this.GenerateDefaultFloor(context, 0, 0, south.HasOpening[Down]);
            }

            if (south.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 4, 1, 6, 4, 7, baseGray);

            if (north.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 4, 8, 6, 4, 14, baseGray);

            this.Fill(context, 0, 3, 0, 0, 3, 15, baseLight);
            this.Fill(context, 7, 3, 0, 7, 3, 15, baseLight);
            this.Fill(context, 1, 3, 0, 7, 3, 0, baseLight);
            this.Fill(context, 1, 3, 15, 6, 3, 15, baseLight);
            this.Fill(context, 0, 2, 0, 0, 2, 15, baseGray);
            this.Fill(context, 7, 2, 0, 7, 2, 15, baseGray);
            this.Fill(context, 1, 2, 0, 7, 2, 0, baseGray);
            this.Fill(context, 1, 2, 15, 6, 2, 15, baseGray);
            this.Fill(context, 0, 1, 0, 0, 1, 15, baseLight);
            this.Fill(context, 7, 1, 0, 7, 1, 15, baseLight);
            this.Fill(context, 1, 1, 0, 7, 1, 0, baseLight);
            this.Fill(context, 1, 1, 15, 6, 1, 15, baseLight);
            this.Fill(context, 1, 1, 1, 1, 1, 2, baseLight);
            this.Fill(context, 6, 1, 1, 6, 1, 2, baseLight);
            this.Fill(context, 1, 3, 1, 1, 3, 2, baseLight);
            this.Fill(context, 6, 3, 1, 6, 3, 2, baseLight);
            this.Fill(context, 1, 1, 13, 1, 1, 14, baseLight);
            this.Fill(context, 6, 1, 13, 6, 1, 14, baseLight);
            this.Fill(context, 1, 3, 13, 1, 3, 14, baseLight);
            this.Fill(context, 6, 3, 13, 6, 3, 14, baseLight);
            this.Fill(context, 2, 1, 6, 2, 3, 6, baseLight);
            this.Fill(context, 5, 1, 6, 5, 3, 6, baseLight);
            this.Fill(context, 2, 1, 9, 2, 3, 9, baseLight);
            this.Fill(context, 5, 1, 9, 5, 3, 9, baseLight);
            this.Fill(context, 3, 2, 6, 4, 2, 6, baseLight);
            this.Fill(context, 3, 2, 9, 4, 2, 9, baseLight);
            this.Fill(context, 2, 2, 7, 2, 2, 8, baseLight);
            this.Fill(context, 5, 2, 7, 5, 2, 8, baseLight);
            this.Place(context, lamp, 2, 2, 5);
            this.Place(context, lamp, 5, 2, 5);
            this.Place(context, lamp, 2, 2, 10);
            this.Place(context, lamp, 5, 2, 10);
            this.Place(context, baseLight, 2, 3, 5);
            this.Place(context, baseLight, 5, 3, 5);
            this.Place(context, baseLight, 2, 3, 10);
            this.Place(context, baseLight, 5, 3, 10);

            if (south.HasOpening[South])
                this.GenerateWaterBox(context, 3, 1, 0, 4, 2, 0);

            if (south.HasOpening[East])
                this.GenerateWaterBox(context, 7, 1, 3, 7, 2, 4);

            if (south.HasOpening[West])
                this.GenerateWaterBox(context, 0, 1, 3, 0, 2, 4);

            if (north.HasOpening[North])
                this.GenerateWaterBox(context, 3, 1, 15, 4, 2, 15);

            if (north.HasOpening[West])
                this.GenerateWaterBox(context, 0, 1, 11, 0, 2, 12);

            if (north.HasOpening[East])
                this.GenerateWaterBox(context, 7, 1, 11, 7, 2, 12);
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentEntryRoom</c>: the grid room behind the entrance.
    /// </summary>
    public sealed class OceanMonumentEntryRoom : OceanMonumentPiece
    {
        internal OceanMonumentEntryRoom(BlockFace orientation, RoomDefinition room) : base(1, orientation, room, 1, 1, 1)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.Fill(context, 0, 3, 0, 2, 3, 7, baseLight);
            this.Fill(context, 5, 3, 0, 7, 3, 7, baseLight);
            this.Fill(context, 0, 2, 0, 1, 2, 7, baseLight);
            this.Fill(context, 6, 2, 0, 7, 2, 7, baseLight);
            this.Fill(context, 0, 1, 0, 0, 1, 7, baseLight);
            this.Fill(context, 7, 1, 0, 7, 1, 7, baseLight);
            this.Fill(context, 0, 1, 7, 7, 3, 7, baseLight);
            this.Fill(context, 1, 1, 0, 2, 3, 0, baseLight);
            this.Fill(context, 5, 1, 0, 6, 3, 0, baseLight);

            if (this.Room.HasOpening[North])
                this.GenerateWaterBox(context, 3, 1, 7, 4, 2, 7);

            if (this.Room.HasOpening[West])
                this.GenerateWaterBox(context, 0, 1, 3, 1, 2, 4);

            if (this.Room.HasOpening[East])
                this.GenerateWaterBox(context, 6, 1, 3, 7, 2, 4);
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentPenthouse</c>: the room on the roof, with an elder guardian.
    /// </summary>
    public sealed class OceanMonumentPenthouse : OceanMonumentPiece
    {
        internal OceanMonumentPenthouse(BlockFace orientation, BlockBox boundingBox) : base(orientation, 1, boundingBox)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            this.Fill(context, 2, -1, 2, 11, -1, 11, baseLight);
            this.Fill(context, 0, -1, 0, 1, -1, 11, baseGray);
            this.Fill(context, 12, -1, 0, 13, -1, 11, baseGray);
            this.Fill(context, 2, -1, 0, 11, -1, 1, baseGray);
            this.Fill(context, 2, -1, 12, 11, -1, 13, baseGray);
            this.Fill(context, 0, 0, 0, 0, 0, 13, baseLight);
            this.Fill(context, 13, 0, 0, 13, 0, 13, baseLight);
            this.Fill(context, 1, 0, 0, 12, 0, 0, baseLight);
            this.Fill(context, 1, 0, 13, 12, 0, 13, baseLight);

            for (var i = 2; i <= 11; i += 3)
            {
                this.Place(context, lamp, 0, 0, i);
                this.Place(context, lamp, 13, 0, i);
                this.Place(context, lamp, i, 0, 0);
            }

            this.Fill(context, 2, 0, 3, 4, 0, 9, baseLight);
            this.Fill(context, 9, 0, 3, 11, 0, 9, baseLight);
            this.Fill(context, 4, 0, 9, 9, 0, 11, baseLight);
            this.Place(context, baseLight, 5, 0, 8);
            this.Place(context, baseLight, 8, 0, 8);
            this.Place(context, baseLight, 10, 0, 10);
            this.Place(context, baseLight, 3, 0, 10);
            this.Fill(context, 3, 0, 3, 3, 0, 7, baseBlack);
            this.Fill(context, 10, 0, 3, 10, 0, 7, baseBlack);
            this.Fill(context, 6, 0, 10, 7, 0, 10, baseBlack);

            for (var x = 3; x <= 10; x += 7)
            {
                for (var z = 2; z <= 8; z += 3)
                    this.Fill(context, x, 0, z, x, 2, z, baseLight);
            }

            this.Fill(context, 5, 0, 10, 5, 2, 10, baseLight);
            this.Fill(context, 8, 0, 10, 8, 2, 10, baseLight);
            this.Fill(context, 6, -1, 7, 7, -1, 8, baseBlack);
            this.GenerateWaterBox(context, 6, -1, 3, 7, -1, 4);
            this.SpawnElder(context, 6, 1, 6);
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentSimpleRoom</c>: a grid room in one of three designs.
    /// </summary>
    public sealed class OceanMonumentSimpleRoom : OceanMonumentPiece
    {
        private readonly int mainDesign;

        internal OceanMonumentSimpleRoom(BlockFace orientation, RoomDefinition room, IRandomSource random) : base(1, orientation, room, 1, 1, 1)
        {
            this.mainDesign = random.NextInt(3);
        }

        public override void PostProcess(StructurePieceContext context)
        {
            var room = this.Room;
            if (room.Index / 25 > 0)
                this.GenerateDefaultFloor(context, 0, 0, room.HasOpening[Down]);

            if (room.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 4, 1, 6, 4, 6, baseGray);

            // Like vanilla, the coin is only flipped for the designs that can have a pillar.
            var centerPillar = this.mainDesign != 0 && context.Random.NextBoolean() && !room.HasOpening[Down] && !room.HasOpening[Up]
                && room.OpeningCount > 1;

            switch (this.mainDesign)
            {
                case 0:
                    this.GenerateCornerPillarsDesign(context);
                    break;
                case 1:
                    this.GenerateLampPillarsDesign(context);
                    break;
                default:
                    this.GenerateDarkDesign(context);
                    break;
            }

            if (centerPillar)
            {
                this.Fill(context, 3, 1, 3, 4, 1, 4, baseLight);
                this.Fill(context, 3, 2, 3, 4, 2, 4, baseGray);
                this.Fill(context, 3, 3, 3, 4, 3, 4, baseLight);
            }
        }

        private void GenerateCornerPillarsDesign(StructurePieceContext context)
        {
            var room = this.Room;
            this.Fill(context, 0, 1, 0, 2, 1, 2, baseLight);
            this.Fill(context, 0, 3, 0, 2, 3, 2, baseLight);
            this.Fill(context, 0, 2, 0, 0, 2, 2, baseGray);
            this.Fill(context, 1, 2, 0, 2, 2, 0, baseGray);
            this.Place(context, lamp, 1, 2, 1);
            this.Fill(context, 5, 1, 0, 7, 1, 2, baseLight);
            this.Fill(context, 5, 3, 0, 7, 3, 2, baseLight);
            this.Fill(context, 7, 2, 0, 7, 2, 2, baseGray);
            this.Fill(context, 5, 2, 0, 6, 2, 0, baseGray);
            this.Place(context, lamp, 6, 2, 1);
            this.Fill(context, 0, 1, 5, 2, 1, 7, baseLight);
            this.Fill(context, 0, 3, 5, 2, 3, 7, baseLight);
            this.Fill(context, 0, 2, 5, 0, 2, 7, baseGray);
            this.Fill(context, 1, 2, 7, 2, 2, 7, baseGray);
            this.Place(context, lamp, 1, 2, 6);
            this.Fill(context, 5, 1, 5, 7, 1, 7, baseLight);
            this.Fill(context, 5, 3, 5, 7, 3, 7, baseLight);
            this.Fill(context, 7, 2, 5, 7, 2, 7, baseGray);
            this.Fill(context, 5, 2, 7, 6, 2, 7, baseGray);
            this.Place(context, lamp, 6, 2, 6);

            if (room.HasOpening[South])
            {
                this.Fill(context, 3, 3, 0, 4, 3, 0, baseLight);
            }
            else
            {
                this.Fill(context, 3, 3, 0, 4, 3, 1, baseLight);
                this.Fill(context, 3, 2, 0, 4, 2, 0, baseGray);
                this.Fill(context, 3, 1, 0, 4, 1, 1, baseLight);
            }

            if (room.HasOpening[North])
            {
                this.Fill(context, 3, 3, 7, 4, 3, 7, baseLight);
            }
            else
            {
                this.Fill(context, 3, 3, 6, 4, 3, 7, baseLight);
                this.Fill(context, 3, 2, 7, 4, 2, 7, baseGray);
                this.Fill(context, 3, 1, 6, 4, 1, 7, baseLight);
            }

            if (room.HasOpening[West])
            {
                this.Fill(context, 0, 3, 3, 0, 3, 4, baseLight);
            }
            else
            {
                this.Fill(context, 0, 3, 3, 1, 3, 4, baseLight);
                this.Fill(context, 0, 2, 3, 0, 2, 4, baseGray);
                this.Fill(context, 0, 1, 3, 1, 1, 4, baseLight);
            }

            if (room.HasOpening[East])
            {
                this.Fill(context, 7, 3, 3, 7, 3, 4, baseLight);
            }
            else
            {
                this.Fill(context, 6, 3, 3, 7, 3, 4, baseLight);
                this.Fill(context, 7, 2, 3, 7, 2, 4, baseGray);
                this.Fill(context, 6, 1, 3, 7, 1, 4, baseLight);
            }
        }

        private void GenerateLampPillarsDesign(StructurePieceContext context)
        {
            var room = this.Room;
            this.Fill(context, 2, 1, 2, 2, 3, 2, baseLight);
            this.Fill(context, 2, 1, 5, 2, 3, 5, baseLight);
            this.Fill(context, 5, 1, 5, 5, 3, 5, baseLight);
            this.Fill(context, 5, 1, 2, 5, 3, 2, baseLight);
            this.Place(context, lamp, 2, 2, 2);
            this.Place(context, lamp, 2, 2, 5);
            this.Place(context, lamp, 5, 2, 5);
            this.Place(context, lamp, 5, 2, 2);
            this.Fill(context, 0, 1, 0, 1, 3, 0, baseLight);
            this.Fill(context, 0, 1, 1, 0, 3, 1, baseLight);
            this.Fill(context, 0, 1, 7, 1, 3, 7, baseLight);
            this.Fill(context, 0, 1, 6, 0, 3, 6, baseLight);
            this.Fill(context, 6, 1, 7, 7, 3, 7, baseLight);
            this.Fill(context, 7, 1, 6, 7, 3, 6, baseLight);
            this.Fill(context, 6, 1, 0, 7, 3, 0, baseLight);
            this.Fill(context, 7, 1, 1, 7, 3, 1, baseLight);
            this.Place(context, baseGray, 1, 2, 0);
            this.Place(context, baseGray, 0, 2, 1);
            this.Place(context, baseGray, 1, 2, 7);
            this.Place(context, baseGray, 0, 2, 6);
            this.Place(context, baseGray, 6, 2, 7);
            this.Place(context, baseGray, 7, 2, 6);
            this.Place(context, baseGray, 6, 2, 0);
            this.Place(context, baseGray, 7, 2, 1);

            if (!room.HasOpening[South])
            {
                this.Fill(context, 1, 3, 0, 6, 3, 0, baseLight);
                this.Fill(context, 1, 2, 0, 6, 2, 0, baseGray);
                this.Fill(context, 1, 1, 0, 6, 1, 0, baseLight);
            }

            if (!room.HasOpening[North])
            {
                this.Fill(context, 1, 3, 7, 6, 3, 7, baseLight);
                this.Fill(context, 1, 2, 7, 6, 2, 7, baseGray);
                this.Fill(context, 1, 1, 7, 6, 1, 7, baseLight);
            }

            if (!room.HasOpening[West])
            {
                this.Fill(context, 0, 3, 1, 0, 3, 6, baseLight);
                this.Fill(context, 0, 2, 1, 0, 2, 6, baseGray);
                this.Fill(context, 0, 1, 1, 0, 1, 6, baseLight);
            }

            if (!room.HasOpening[East])
            {
                this.Fill(context, 7, 3, 1, 7, 3, 6, baseLight);
                this.Fill(context, 7, 2, 1, 7, 2, 6, baseGray);
                this.Fill(context, 7, 1, 1, 7, 1, 6, baseLight);
            }
        }

        private void GenerateDarkDesign(StructurePieceContext context)
        {
            var room = this.Room;
            this.Fill(context, 0, 1, 0, 0, 1, 7, baseLight);
            this.Fill(context, 7, 1, 0, 7, 1, 7, baseLight);
            this.Fill(context, 1, 1, 0, 6, 1, 0, baseLight);
            this.Fill(context, 1, 1, 7, 6, 1, 7, baseLight);
            this.Fill(context, 0, 2, 0, 0, 2, 7, baseBlack);
            this.Fill(context, 7, 2, 0, 7, 2, 7, baseBlack);
            this.Fill(context, 1, 2, 0, 6, 2, 0, baseBlack);
            this.Fill(context, 1, 2, 7, 6, 2, 7, baseBlack);
            this.Fill(context, 0, 3, 0, 0, 3, 7, baseLight);
            this.Fill(context, 7, 3, 0, 7, 3, 7, baseLight);
            this.Fill(context, 1, 3, 0, 6, 3, 0, baseLight);
            this.Fill(context, 1, 3, 7, 6, 3, 7, baseLight);
            this.Fill(context, 0, 1, 3, 0, 2, 4, baseBlack);
            this.Fill(context, 7, 1, 3, 7, 2, 4, baseBlack);
            this.Fill(context, 3, 1, 0, 4, 2, 0, baseBlack);
            this.Fill(context, 3, 1, 7, 4, 2, 7, baseBlack);

            if (room.HasOpening[South])
                this.GenerateWaterBox(context, 3, 1, 0, 4, 2, 0);

            if (room.HasOpening[North])
                this.GenerateWaterBox(context, 3, 1, 7, 4, 2, 7);

            if (room.HasOpening[West])
                this.GenerateWaterBox(context, 0, 1, 3, 0, 2, 4);

            if (room.HasOpening[East])
                this.GenerateWaterBox(context, 7, 1, 3, 7, 2, 4);
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentSimpleTopRoom</c>: a top floor grid room with wet sponges.
    /// </summary>
    public sealed class OceanMonumentSimpleTopRoom : OceanMonumentPiece
    {
        internal OceanMonumentSimpleTopRoom(BlockFace orientation, RoomDefinition room) : base(1, orientation, room, 1, 1, 1)
        {
        }

        public override void PostProcess(StructurePieceContext context)
        {
            if (this.Room.Index / 25 > 0)
                this.GenerateDefaultFloor(context, 0, 0, this.Room.HasOpening[Down]);

            if (this.Room.Connections[Up] is null)
                this.GenerateBoxOnFillOnly(context, 1, 4, 1, 6, 4, 6, baseGray);

            var wetSponge = BlocksRegistry.Get(Material.WetSponge);
            for (var x = 1; x <= 6; x++)
            {
                for (var z = 1; z <= 6; z++)
                {
                    if (context.Random.NextInt(3) == 0)
                        continue;

                    var minY = 2 + (context.Random.NextInt(4) == 0 ? 0 : 1);
                    this.Fill(context, x, minY, z, x, 3, z, wetSponge);
                }
            }

            this.Fill(context, 0, 1, 0, 0, 1, 7, baseLight);
            this.Fill(context, 7, 1, 0, 7, 1, 7, baseLight);
            this.Fill(context, 1, 1, 0, 6, 1, 0, baseLight);
            this.Fill(context, 1, 1, 7, 6, 1, 7, baseLight);
            this.Fill(context, 0, 2, 0, 0, 2, 7, baseBlack);
            this.Fill(context, 7, 2, 0, 7, 2, 7, baseBlack);
            this.Fill(context, 1, 2, 0, 6, 2, 0, baseBlack);
            this.Fill(context, 1, 2, 7, 6, 2, 7, baseBlack);
            this.Fill(context, 0, 3, 0, 0, 3, 7, baseLight);
            this.Fill(context, 7, 3, 0, 7, 3, 7, baseLight);
            this.Fill(context, 1, 3, 0, 6, 3, 0, baseLight);
            this.Fill(context, 1, 3, 7, 6, 3, 7, baseLight);
            this.Fill(context, 0, 1, 3, 0, 2, 4, baseBlack);
            this.Fill(context, 7, 1, 3, 7, 2, 4, baseBlack);
            this.Fill(context, 3, 1, 0, 4, 2, 0, baseBlack);
            this.Fill(context, 3, 1, 7, 4, 2, 7, baseBlack);

            if (this.Room.HasOpening[South])
                this.GenerateWaterBox(context, 3, 1, 0, 4, 2, 0);
        }
    }

    /// <summary>
    /// Vanilla's <c>OceanMonumentWingRoom</c>: the room inside a wing, in one of two designs, with an elder guardian.
    /// </summary>
    public sealed class OceanMonumentWingRoom : OceanMonumentPiece
    {
        private readonly int mainDesign;

        internal OceanMonumentWingRoom(BlockFace orientation, BlockBox boundingBox, int randomValue) : base(orientation, 1, boundingBox)
        {
            this.mainDesign = randomValue & 1;
        }

        public override void PostProcess(StructurePieceContext context)
        {
            if (this.mainDesign == 0)
            {
                for (var i = 0; i < 4; i++)
                    this.Fill(context, 10 - i, 3 - i, 20 - i, 12 + i, 3 - i, 20, baseLight);

                this.Fill(context, 7, 0, 6, 15, 0, 16, baseLight);
                this.Fill(context, 6, 0, 6, 6, 3, 20, baseLight);
                this.Fill(context, 16, 0, 6, 16, 3, 20, baseLight);
                this.Fill(context, 7, 1, 7, 7, 1, 20, baseLight);
                this.Fill(context, 15, 1, 7, 15, 1, 20, baseLight);
                this.Fill(context, 7, 1, 6, 9, 3, 6, baseLight);
                this.Fill(context, 13, 1, 6, 15, 3, 6, baseLight);
                this.Fill(context, 8, 1, 7, 9, 1, 7, baseLight);
                this.Fill(context, 13, 1, 7, 14, 1, 7, baseLight);
                this.Fill(context, 9, 0, 5, 13, 0, 5, baseLight);
                this.Fill(context, 10, 0, 7, 12, 0, 7, baseBlack);
                this.Fill(context, 8, 0, 10, 8, 0, 12, baseBlack);
                this.Fill(context, 14, 0, 10, 14, 0, 12, baseBlack);

                for (var z = 18; z >= 7; z -= 3)
                {
                    this.Place(context, lamp, 6, 3, z);
                    this.Place(context, lamp, 16, 3, z);
                }

                this.Place(context, lamp, 10, 0, 10);
                this.Place(context, lamp, 12, 0, 10);
                this.Place(context, lamp, 10, 0, 12);
                this.Place(context, lamp, 12, 0, 12);
                this.Place(context, lamp, 8, 3, 6);
                this.Place(context, lamp, 14, 3, 6);
                this.Place(context, baseLight, 4, 2, 4);
                this.Place(context, lamp, 4, 1, 4);
                this.Place(context, baseLight, 4, 0, 4);
                this.Place(context, baseLight, 18, 2, 4);
                this.Place(context, lamp, 18, 1, 4);
                this.Place(context, baseLight, 18, 0, 4);
                this.Place(context, baseLight, 4, 2, 18);
                this.Place(context, lamp, 4, 1, 18);
                this.Place(context, baseLight, 4, 0, 18);
                this.Place(context, baseLight, 18, 2, 18);
                this.Place(context, lamp, 18, 1, 18);
                this.Place(context, baseLight, 18, 0, 18);
                this.Place(context, baseLight, 9, 7, 20);
                this.Place(context, baseLight, 13, 7, 20);
                this.Fill(context, 6, 0, 21, 7, 4, 21, baseLight);
                this.Fill(context, 15, 0, 21, 16, 4, 21, baseLight);
                this.SpawnElder(context, 11, 2, 16);
                return;
            }

            this.Fill(context, 9, 3, 18, 13, 3, 20, baseLight);
            this.Fill(context, 9, 0, 18, 9, 2, 18, baseLight);
            this.Fill(context, 13, 0, 18, 13, 2, 18, baseLight);

            for (var x = 9; x <= 13; x += 4)
            {
                this.Place(context, baseLight, x, 6, 20);
                this.Place(context, lamp, x, 5, 20);
                this.Place(context, baseLight, x, 4, 20);
            }

            this.Fill(context, 7, 3, 7, 15, 3, 14, baseLight);

            for (var x = 10; x <= 12; x += 2)
            {
                this.Fill(context, x, 0, 10, x, 6, 10, baseLight);
                this.Fill(context, x, 0, 12, x, 6, 12, baseLight);
                this.Place(context, lamp, x, 0, 10);
                this.Place(context, lamp, x, 0, 12);
                this.Place(context, lamp, x, 4, 10);
                this.Place(context, lamp, x, 4, 12);
            }

            for (var x = 8; x <= 14; x += 6)
            {
                this.Fill(context, x, 0, 7, x, 2, 7, baseLight);
                this.Fill(context, x, 0, 14, x, 2, 14, baseLight);
            }

            this.Fill(context, 8, 3, 8, 8, 3, 13, baseBlack);
            this.Fill(context, 14, 3, 8, 14, 3, 13, baseBlack);
            this.SpawnElder(context, 11, 5, 13);
        }
    }
}
