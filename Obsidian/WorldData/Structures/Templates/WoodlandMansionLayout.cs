using Obsidian.API.World.Generator.RandomSources;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Structures.Templates;

/// <summary>
/// Lays out a woodland mansion: a random floor plan on a grid of 8 block cells, then the pieces for its walls, roofs,
/// corridors and rooms, like vanilla's <c>WoodlandMansionPieces</c>.
/// </summary>
internal sealed class WoodlandMansionLayout
{
    // Cell values of the floor plans.
    private const int Clear = 0;
    private const int Corridor = 1;
    private const int Room = 2;
    private const int StartRoom = 3;
    private const int Blocked = 5;

    // Flags of the room grids: the room size, its origin cell, door, stairs and corridor cells, and the room id below.
    private const int Room1x1 = 0x10000;
    private const int Room1x2 = 0x20000;
    private const int Room2x2 = 0x40000;
    private const int RoomOriginFlag = 0x100000;
    private const int RoomDoorFlag = 0x200000;
    private const int RoomStairsFlag = 0x400000;
    private const int RoomCorridorFlag = 0x800000;
    private const int RoomTypeMask = 0xF0000;
    private const int RoomIdMask = 0xFFFF;

    private static readonly BlockFace[] horizontal = [BlockFace.North, BlockFace.East, BlockFace.South, BlockFace.West];

    // Vanilla Direction.from2DDataValue order.
    private static readonly BlockFace[] byDataValue = [BlockFace.South, BlockFace.West, BlockFace.North, BlockFace.East];

    private readonly IRandomSource random;
    private readonly Grid baseGrid;
    private readonly Grid thirdFloorGrid;
    private readonly Grid[] floorRooms;
    private readonly int entranceX = 7;
    private readonly int entranceY = 4;
    private int startX;
    private int startY;

    /// <summary>Vanilla <c>MansionGrid</c>'s constructor: the floor plans and room assignment.</summary>
    private WoodlandMansionLayout(IRandomSource random)
    {
        this.random = random;
        this.baseGrid = new Grid(11, 11, Blocked);
        this.baseGrid.Set(this.entranceX, this.entranceY, this.entranceX + 1, this.entranceY + 1, StartRoom);
        this.baseGrid.Set(this.entranceX - 1, this.entranceY, this.entranceX - 1, this.entranceY + 1, Room);
        this.baseGrid.Set(this.entranceX + 2, this.entranceY - 2, this.entranceX + 3, this.entranceY + 3, Blocked);
        this.baseGrid.Set(this.entranceX + 1, this.entranceY - 2, this.entranceX + 1, this.entranceY - 1, Corridor);
        this.baseGrid.Set(this.entranceX + 1, this.entranceY + 2, this.entranceX + 1, this.entranceY + 3, Corridor);
        this.baseGrid.Set(this.entranceX - 1, this.entranceY - 1, Corridor);
        this.baseGrid.Set(this.entranceX - 1, this.entranceY + 2, Corridor);
        this.baseGrid.Set(0, 0, 11, 1, Blocked);
        this.baseGrid.Set(0, 9, 11, 11, Blocked);
        this.RecursiveCorridor(this.baseGrid, this.entranceX, this.entranceY - 2, BlockFace.West, 6);
        this.RecursiveCorridor(this.baseGrid, this.entranceX, this.entranceY + 3, BlockFace.West, 6);
        this.RecursiveCorridor(this.baseGrid, this.entranceX - 2, this.entranceY - 1, BlockFace.West, 3);
        this.RecursiveCorridor(this.baseGrid, this.entranceX - 2, this.entranceY + 2, BlockFace.West, 3);

        while (CleanEdges(this.baseGrid))
        {
        }

        this.floorRooms = [new Grid(11, 11, Blocked), new Grid(11, 11, Blocked), new Grid(11, 11, Blocked)];
        this.IdentifyRooms(this.baseGrid, this.floorRooms[0]);
        this.IdentifyRooms(this.baseGrid, this.floorRooms[1]);
        this.floorRooms[0].Set(this.entranceX + 1, this.entranceY, this.entranceX + 1, this.entranceY + 1, RoomCorridorFlag);
        this.floorRooms[1].Set(this.entranceX + 1, this.entranceY, this.entranceX + 1, this.entranceY + 1, RoomCorridorFlag);
        this.thirdFloorGrid = new Grid(this.baseGrid.Width, this.baseGrid.Height, Blocked);
        this.SetupThirdFloor();
        this.IdentifyRooms(this.thirdFloorGrid, this.floorRooms[2]);
    }

    /// <summary>Vanilla <c>generateMansion</c>: the mansion's pieces in vanilla's order.</summary>
    public static List<WoodlandMansionPiece> Generate(Vector position, StructureRotation rotation, IRandomSource random)
    {
        var layout = new WoodlandMansionLayout(random);
        List<WoodlandMansionPiece> pieces = [];
        layout.CreateMansion(position, rotation, pieces);
        return pieces;
    }

    private static bool IsHouse(Grid grid, int x, int y) => grid.Get(x, y) is Corridor or Room or StartRoom or 4;

    private bool IsRoomId(int x, int y, int floor, int roomId) => (this.floorRooms[floor].Get(x, y) & RoomIdMask) == roomId;

    private BlockFace? Get1x2RoomDirection(int x, int y, int floor, int roomId)
    {
        foreach (var direction in horizontal)
        {
            var step = direction.ToVector();
            if (this.IsRoomId(x + step.X, y + step.Z, floor, roomId))
                return direction;
        }

        return null;
    }

    private void RecursiveCorridor(Grid grid, int x, int y, BlockFace direction, int length)
    {
        if (length <= 0)
            return;

        var step = direction.ToVector();
        grid.Set(x, y, Corridor);
        grid.SetIf(x + step.X, y + step.Z, Clear, Corridor);

        for (var attempt = 0; attempt < 8; attempt++)
        {
            var turn = byDataValue[this.random.NextInt(4)];
            if (turn == direction.Opposite() || turn == BlockFace.East && this.random.NextBoolean())
                continue;

            var nextX = x + step.X;
            var nextY = y + step.Z;
            var turnStep = turn.ToVector();
            if (grid.Get(nextX + turnStep.X, nextY + turnStep.Z) == Clear && grid.Get(nextX + turnStep.X * 2, nextY + turnStep.Z * 2) == Clear)
            {
                this.RecursiveCorridor(grid, x + step.X + turnStep.X, y + step.Z + turnStep.Z, turn, length - 1);
                break;
            }
        }

        var clockwise = direction.ClockWise().ToVector();
        var counterClockwise = direction.CounterClockWise().ToVector();
        grid.SetIf(x + clockwise.X, y + clockwise.Z, Clear, Room);
        grid.SetIf(x + counterClockwise.X, y + counterClockwise.Z, Clear, Room);
        grid.SetIf(x + step.X + clockwise.X, y + step.Z + clockwise.Z, Clear, Room);
        grid.SetIf(x + step.X + counterClockwise.X, y + step.Z + counterClockwise.Z, Clear, Room);
        grid.SetIf(x + step.X * 2, y + step.Z * 2, Clear, Room);
        grid.SetIf(x + clockwise.X * 2, y + clockwise.Z * 2, Clear, Room);
        grid.SetIf(x + counterClockwise.X * 2, y + counterClockwise.Z * 2, Clear, Room);
    }

    /// <summary>Vanilla <c>cleanEdges</c>: fills clear cells mostly surrounded by the house.</summary>
    private static bool CleanEdges(Grid grid)
    {
        var changed = false;
        for (var y = 0; y < grid.Height; y++)
        {
            for (var x = 0; x < grid.Width; x++)
            {
                if (grid.Get(x, y) != Clear)
                    continue;

                var sides = (IsHouse(grid, x + 1, y) ? 1 : 0) + (IsHouse(grid, x - 1, y) ? 1 : 0)
                    + (IsHouse(grid, x, y + 1) ? 1 : 0) + (IsHouse(grid, x, y - 1) ? 1 : 0);
                if (sides >= 3)
                {
                    grid.Set(x, y, Room);
                    changed = true;
                }
                else if (sides == 2)
                {
                    var corners = (IsHouse(grid, x + 1, y + 1) ? 1 : 0) + (IsHouse(grid, x - 1, y + 1) ? 1 : 0)
                        + (IsHouse(grid, x + 1, y - 1) ? 1 : 0) + (IsHouse(grid, x - 1, y - 1) ? 1 : 0);
                    if (corners <= 1)
                    {
                        grid.Set(x, y, Room);
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }

    /// <summary>
    /// Vanilla <c>setupThirdFloor</c>: picks a 1x2 room of the second floor for the stairs and grows corridors from them.
    /// </summary>
    private void SetupThirdFloor()
    {
        List<(int X, int Y)> candidates = [];
        var secondFloor = this.floorRooms[1];

        for (var y = 0; y < this.thirdFloorGrid.Height; y++)
        {
            for (var x = 0; x < this.thirdFloorGrid.Width; x++)
            {
                var cell = secondFloor.Get(x, y);
                if ((cell & RoomTypeMask) == Room1x2 && (cell & RoomDoorFlag) == RoomDoorFlag)
                    candidates.Add((x, y));
            }
        }

        if (candidates.Count == 0)
        {
            this.thirdFloorGrid.Set(0, 0, this.thirdFloorGrid.Width, this.thirdFloorGrid.Height, Blocked);
            return;
        }

        var (stairsX, stairsY) = candidates[this.random.NextInt(candidates.Count)];
        var stairsCell = secondFloor.Get(stairsX, stairsY);
        secondFloor.Set(stairsX, stairsY, stairsCell | RoomStairsFlag);
        var direction = this.Get1x2RoomDirection(stairsX, stairsY, 1, stairsCell & RoomIdMask)!.Value.ToVector();
        var otherX = stairsX + direction.X;
        var otherY = stairsY + direction.Z;

        for (var y = 0; y < this.thirdFloorGrid.Height; y++)
        {
            for (var x = 0; x < this.thirdFloorGrid.Width; x++)
            {
                if (!IsHouse(this.baseGrid, x, y))
                {
                    this.thirdFloorGrid.Set(x, y, Blocked);
                }
                else if (x == stairsX && y == stairsY)
                {
                    this.thirdFloorGrid.Set(x, y, StartRoom);
                }
                else if (x == otherX && y == otherY)
                {
                    this.thirdFloorGrid.Set(x, y, StartRoom);
                    this.floorRooms[2].Set(x, y, RoomCorridorFlag);
                }
            }
        }

        List<BlockFace> exits = [];
        foreach (var face in horizontal)
        {
            var step = face.ToVector();
            if (this.thirdFloorGrid.Get(otherX + step.X, otherY + step.Z) == Clear)
                exits.Add(face);
        }

        if (exits.Count == 0)
        {
            this.thirdFloorGrid.Set(0, 0, this.thirdFloorGrid.Width, this.thirdFloorGrid.Height, Blocked);
            secondFloor.Set(stairsX, stairsY, stairsCell);
            return;
        }

        var exit = exits[this.random.NextInt(exits.Count)];
        var exitStep = exit.ToVector();
        this.RecursiveCorridor(this.thirdFloorGrid, otherX + exitStep.X, otherY + exitStep.Z, exit, 4);

        while (CleanEdges(this.thirdFloorGrid))
        {
        }
    }

    /// <summary>
    /// Vanilla <c>identifyRooms</c>: groups the room cells, in a random order, into 2x2, 1x2 and 1x1 rooms with a door cell.
    /// </summary>
    private void IdentifyRooms(Grid plan, Grid rooms)
    {
        List<(int X, int Y)> cells = [];
        for (var y = 0; y < plan.Height; y++)
        {
            for (var x = 0; x < plan.Width; x++)
            {
                if (plan.Get(x, y) == Room)
                    cells.Add((x, y));
            }
        }

        FeatureHelpers.Shuffle(cells, this.random);
        var roomId = 10;

        foreach (var (x, y) in cells)
        {
            if (rooms.Get(x, y) != Clear)
                continue;

            var minX = x;
            var maxX = x;
            var minY = y;
            var maxY = y;
            var size = Room1x1;

            if (rooms.Get(x + 1, y) == Clear && rooms.Get(x, y + 1) == Clear && rooms.Get(x + 1, y + 1) == Clear
                && plan.Get(x + 1, y) == Room && plan.Get(x, y + 1) == Room && plan.Get(x + 1, y + 1) == Room)
            {
                maxX++;
                maxY++;
                size = Room2x2;
            }
            else if (rooms.Get(x - 1, y) == Clear && rooms.Get(x, y + 1) == Clear && rooms.Get(x - 1, y + 1) == Clear
                && plan.Get(x - 1, y) == Room && plan.Get(x, y + 1) == Room && plan.Get(x - 1, y + 1) == Room)
            {
                minX--;
                maxY++;
                size = Room2x2;
            }
            else if (rooms.Get(x - 1, y) == Clear && rooms.Get(x, y - 1) == Clear && rooms.Get(x - 1, y - 1) == Clear
                && plan.Get(x - 1, y) == Room && plan.Get(x, y - 1) == Room && plan.Get(x - 1, y - 1) == Room)
            {
                minX--;
                minY--;
                size = Room2x2;
            }
            else if (rooms.Get(x + 1, y) == Clear && plan.Get(x + 1, y) == Room)
            {
                maxX++;
                size = Room1x2;
            }
            else if (rooms.Get(x, y + 1) == Clear && plan.Get(x, y + 1) == Room)
            {
                maxY++;
                size = Room1x2;
            }
            else if (rooms.Get(x - 1, y) == Clear && plan.Get(x - 1, y) == Room)
            {
                minX--;
                size = Room1x2;
            }
            else if (rooms.Get(x, y - 1) == Clear && plan.Get(x, y - 1) == Room)
            {
                minY--;
                size = Room1x2;
            }

            // The door cell: a random corner next to a corridor, trying the others in vanilla's order.
            var doorX = this.random.NextBoolean() ? minX : maxX;
            var doorY = this.random.NextBoolean() ? minY : maxY;
            var door = RoomDoorFlag;
            if (!plan.EdgesTo(doorX, doorY, Corridor))
            {
                doorX = doorX == minX ? maxX : minX;
                doorY = doorY == minY ? maxY : minY;
                if (!plan.EdgesTo(doorX, doorY, Corridor))
                {
                    doorY = doorY == minY ? maxY : minY;
                    if (!plan.EdgesTo(doorX, doorY, Corridor))
                    {
                        doorX = doorX == minX ? maxX : minX;
                        doorY = doorY == minY ? maxY : minY;
                        if (!plan.EdgesTo(doorX, doorY, Corridor))
                        {
                            door = 0;
                            doorX = minX;
                            doorY = minY;
                        }
                    }
                }
            }

            for (var cellY = minY; cellY <= maxY; cellY++)
            {
                for (var cellX = minX; cellX <= maxX; cellX++)
                {
                    rooms.Set(cellX, cellY, cellX == doorX && cellY == doorY ? RoomOriginFlag | door | size | roomId : size | roomId);
                }
            }

            roomId++;
        }
    }

    /// <summary>Vanilla <c>MansionPiecePlacer.createMansion</c>.</summary>
    private void CreateMansion(Vector origin, StructureRotation rotation, List<WoodlandMansionPiece> pieces)
    {
        var firstFloor = new PlacementData { Position = origin, Rotation = rotation, WallType = "wall_flat" };
        Entrance(pieces, firstFloor);
        var secondFloor = new PlacementData { Position = firstFloor.Position + (0, 8, 0), Rotation = firstFloor.Rotation, WallType = "wall_window" };

        this.startX = this.entranceX + 1;
        this.startY = this.entranceY + 1;
        var endX = this.entranceX + 1;
        var endY = this.entranceY;
        TraverseOuterWalls(pieces, firstFloor, this.baseGrid, BlockFace.South, this.startX, this.startY, endX, endY);
        TraverseOuterWalls(pieces, secondFloor, this.baseGrid, BlockFace.South, this.startX, this.startY, endX, endY);

        var thirdFloor = new PlacementData { Position = firstFloor.Position + (0, 19, 0), Rotation = firstFloor.Rotation, WallType = "wall_window" };
        var found = false;
        for (var y = 0; y < this.thirdFloorGrid.Height && !found; y++)
        {
            for (var x = this.thirdFloorGrid.Width - 1; x >= 0 && !found; x--)
            {
                if (!IsHouse(this.thirdFloorGrid, x, y))
                    continue;

                thirdFloor.Position = Relative(thirdFloor.Position, rotation.Rotate(BlockFace.South), 8 + (y - this.startY) * 8);
                thirdFloor.Position = Relative(thirdFloor.Position, rotation.Rotate(BlockFace.East), (x - this.startX) * 8);
                TraverseWallPiece(pieces, thirdFloor);
                TraverseOuterWalls(pieces, thirdFloor, this.thirdFloorGrid, BlockFace.South, x, y, x, y);
                found = true;
            }
        }

        this.CreateRoof(pieces, origin + (0, 16, 0), rotation, this.baseGrid, this.thirdFloorGrid);
        this.CreateRoof(pieces, origin + (0, 27, 0), rotation, this.thirdFloorGrid, null);

        RoomCollection[] collections = [RoomCollection.FirstFloor, RoomCollection.SecondFloor, RoomCollection.SecondFloor];
        List<BlockFace> doors = [];

        for (var floor = 0; floor < 3; floor++)
        {
            var floorOrigin = origin + (0, 8 * floor + (floor == 2 ? 3 : 0), 0);
            var rooms = this.floorRooms[floor];
            var plan = floor == 2 ? this.thirdFloorGrid : this.baseGrid;
            var carpetSouth = floor == 0 ? "carpet_south_1" : "carpet_south_2";
            var carpetWest = floor == 0 ? "carpet_west_1" : "carpet_west_2";

            for (var y = 0; y < plan.Height; y++)
            {
                for (var x = 0; x < plan.Width; x++)
                {
                    if (plan.Get(x, y) != Corridor)
                        continue;

                    var position = Relative(floorOrigin, rotation.Rotate(BlockFace.South), 8 + (y - this.startY) * 8);
                    position = Relative(position, rotation.Rotate(BlockFace.East), (x - this.startX) * 8);
                    pieces.Add(new WoodlandMansionPiece("corridor_floor", position, rotation));

                    if (plan.Get(x, y - 1) == Corridor || (rooms.Get(x, y - 1) & RoomCorridorFlag) == RoomCorridorFlag)
                        pieces.Add(new WoodlandMansionPiece("carpet_north", Relative(position, rotation.Rotate(BlockFace.East), 1) + (0, 1, 0), rotation));

                    if (plan.Get(x + 1, y) == Corridor || (rooms.Get(x + 1, y) & RoomCorridorFlag) == RoomCorridorFlag)
                    {
                        var carpet = Relative(Relative(position, rotation.Rotate(BlockFace.South), 1), rotation.Rotate(BlockFace.East), 5) + (0, 1, 0);
                        pieces.Add(new WoodlandMansionPiece("carpet_east", carpet, rotation));
                    }

                    if (plan.Get(x, y + 1) == Corridor || (rooms.Get(x, y + 1) & RoomCorridorFlag) == RoomCorridorFlag)
                    {
                        var carpet = Relative(Relative(position, rotation.Rotate(BlockFace.South), 5), rotation.Rotate(BlockFace.West), 1);
                        pieces.Add(new WoodlandMansionPiece(carpetSouth, carpet, rotation));
                    }

                    if (plan.Get(x - 1, y) == Corridor || (rooms.Get(x - 1, y) & RoomCorridorFlag) == RoomCorridorFlag)
                    {
                        var carpet = Relative(Relative(position, rotation.Rotate(BlockFace.West), 1), rotation.Rotate(BlockFace.North), 1);
                        pieces.Add(new WoodlandMansionPiece(carpetWest, carpet, rotation));
                    }
                }
            }

            var wall = floor == 0 ? "indoors_wall_1" : "indoors_wall_2";
            var doorName = floor == 0 ? "indoors_door_1" : "indoors_door_2";

            for (var y = 0; y < plan.Height; y++)
            {
                for (var x = 0; x < plan.Width; x++)
                {
                    var stairsCorridor = floor == 2 && plan.Get(x, y) == StartRoom;
                    if (plan.Get(x, y) != Room && !stairsCorridor)
                        continue;

                    var cell = rooms.Get(x, y);
                    var size = cell & RoomTypeMask;
                    var roomId = cell & RoomIdMask;
                    stairsCorridor = stairsCorridor && (cell & RoomCorridorFlag) == RoomCorridorFlag;
                    doors.Clear();

                    if ((cell & RoomDoorFlag) == RoomDoorFlag)
                    {
                        foreach (var face in horizontal)
                        {
                            var step = face.ToVector();
                            if (plan.Get(x + step.X, y + step.Z) == Corridor)
                                doors.Add(face);
                        }
                    }

                    BlockFace? door = null;
                    if (doors.Count > 0)
                        door = doors[this.random.NextInt(doors.Count)];
                    else if ((cell & RoomOriginFlag) == RoomOriginFlag)
                        door = BlockFace.Up;

                    var position = Relative(floorOrigin, rotation.Rotate(BlockFace.South), 8 + (y - this.startY) * 8);
                    position = Relative(position, rotation.Rotate(BlockFace.East), -1 + (x - this.startX) * 8);

                    if (IsHouse(plan, x - 1, y) && !this.IsRoomId(x - 1, y, floor, roomId))
                        pieces.Add(new WoodlandMansionPiece(door == BlockFace.West ? doorName : wall, position, rotation));

                    if (plan.Get(x + 1, y) == Corridor && !stairsCorridor)
                        pieces.Add(new WoodlandMansionPiece(door == BlockFace.East ? doorName : wall, Relative(position, rotation.Rotate(BlockFace.East), 8), rotation));

                    if (IsHouse(plan, x, y + 1) && !this.IsRoomId(x, y + 1, floor, roomId))
                    {
                        var wallPosition = Relative(Relative(position, rotation.Rotate(BlockFace.South), 7), rotation.Rotate(BlockFace.East), 7);
                        pieces.Add(new WoodlandMansionPiece(door == BlockFace.South ? doorName : wall, wallPosition, rotation.GetRotated(StructureRotation.Clockwise90)));
                    }

                    if (plan.Get(x, y - 1) == Corridor && !stairsCorridor)
                    {
                        var wallPosition = Relative(Relative(position, rotation.Rotate(BlockFace.North), 1), rotation.Rotate(BlockFace.East), 7);
                        pieces.Add(new WoodlandMansionPiece(door == BlockFace.North ? doorName : wall, wallPosition, rotation.GetRotated(StructureRotation.Clockwise90)));
                    }

                    if (size == Room1x1)
                    {
                        this.AddRoom1x1(pieces, position, rotation, door, collections[floor]);
                    }
                    else if (size == Room1x2 && door is not null)
                    {
                        var roomDirection = this.Get1x2RoomDirection(x, y, floor, roomId);
                        var hasStairs = (cell & RoomStairsFlag) == RoomStairsFlag;
                        this.AddRoom1x2(pieces, position, rotation, roomDirection, door.Value, collections[floor], hasStairs);
                    }
                    else if (size == Room2x2 && door is not null && door != BlockFace.Up)
                    {
                        var side = door.Value.ClockWise();
                        var sideStep = side.ToVector();
                        if (!this.IsRoomId(x + sideStep.X, y + sideStep.Z, floor, roomId))
                            side = side.Opposite();

                        this.AddRoom2x2(pieces, position, rotation, side, door.Value, collections[floor]);
                    }
                    else if (size == Room2x2 && door == BlockFace.Up)
                    {
                        pieces.Add(new WoodlandMansionPiece(RoomCollection.Get2x2Secret(), Relative(position, rotation.Rotate(BlockFace.East), 1), rotation));
                    }
                }
            }
        }
    }

    private static void TraverseOuterWalls(List<WoodlandMansionPiece> pieces, PlacementData data, Grid grid, BlockFace direction, int startX, int startY,
        int endX, int endY)
    {
        var x = startX;
        var y = startY;
        var endDirection = direction;

        do
        {
            var step = direction.ToVector();
            if (!IsHouse(grid, x + step.X, y + step.Z))
            {
                TraverseTurn(pieces, data);
                direction = direction.ClockWise();
                if (x != endX || y != endY || endDirection != direction)
                    TraverseWallPiece(pieces, data);
            }
            else if (IsHouse(grid, x + step.X + direction.CounterClockWise().ToVector().X, y + step.Z + direction.CounterClockWise().ToVector().Z))
            {
                TraverseInnerTurn(data);
                x += step.X;
                y += step.Z;
                direction = direction.CounterClockWise();
            }
            else
            {
                x += step.X;
                y += step.Z;
                if (x != endX || y != endY || endDirection != direction)
                    TraverseWallPiece(pieces, data);
            }
        }
        while (x != endX || y != endY || endDirection != direction);
    }

    private void CreateRoof(List<WoodlandMansionPiece> pieces, Vector origin, StructureRotation rotation, Grid plan, Grid? above)
    {
        var east = rotation.Rotate(BlockFace.East);
        var south = rotation.Rotate(BlockFace.South);
        var west = rotation.Rotate(BlockFace.West);
        var north = rotation.Rotate(BlockFace.North);

        for (var y = 0; y < plan.Height; y++)
        {
            for (var x = 0; x < plan.Width; x++)
            {
                var position = Relative(Relative(origin, south, 8 + (y - this.startY) * 8), east, (x - this.startX) * 8);
                var covered = above is not null && IsHouse(above, x, y);
                if (!IsHouse(plan, x, y) || covered)
                    continue;

                pieces.Add(new WoodlandMansionPiece("roof", position + (0, 3, 0), rotation));
                if (!IsHouse(plan, x + 1, y))
                    pieces.Add(new WoodlandMansionPiece("roof_front", Relative(position, east, 6), rotation));

                if (!IsHouse(plan, x - 1, y))
                    pieces.Add(new WoodlandMansionPiece("roof_front", Relative(Relative(position, east, 0), south, 7), rotation.GetRotated(StructureRotation.Clockwise180)));

                if (!IsHouse(plan, x, y - 1))
                    pieces.Add(new WoodlandMansionPiece("roof_front", Relative(position, west, 1), rotation.GetRotated(StructureRotation.CounterClockwise90)));

                if (!IsHouse(plan, x, y + 1))
                    pieces.Add(new WoodlandMansionPiece("roof_front", Relative(Relative(position, east, 6), south, 6), rotation.GetRotated(StructureRotation.Clockwise90)));
            }
        }

        if (above is not null)
        {
            for (var y = 0; y < plan.Height; y++)
            {
                for (var x = 0; x < plan.Width; x++)
                {
                    var position = Relative(Relative(origin, south, 8 + (y - this.startY) * 8), east, (x - this.startX) * 8);
                    if (!IsHouse(plan, x, y) || !IsHouse(above, x, y))
                        continue;

                    if (!IsHouse(plan, x + 1, y))
                        pieces.Add(new WoodlandMansionPiece("small_wall", Relative(position, east, 7), rotation));

                    if (!IsHouse(plan, x - 1, y))
                        pieces.Add(new WoodlandMansionPiece("small_wall", Relative(Relative(position, west, 1), south, 6), rotation.GetRotated(StructureRotation.Clockwise180)));

                    if (!IsHouse(plan, x, y - 1))
                        pieces.Add(new WoodlandMansionPiece("small_wall", Relative(Relative(position, west, 0), north, 1), rotation.GetRotated(StructureRotation.CounterClockwise90)));

                    if (!IsHouse(plan, x, y + 1))
                        pieces.Add(new WoodlandMansionPiece("small_wall", Relative(Relative(position, east, 6), south, 7), rotation.GetRotated(StructureRotation.Clockwise90)));

                    if (!IsHouse(plan, x + 1, y))
                    {
                        if (!IsHouse(plan, x, y - 1))
                            pieces.Add(new WoodlandMansionPiece("small_wall_corner", Relative(Relative(position, east, 7), north, 2), rotation));

                        if (!IsHouse(plan, x, y + 1))
                            pieces.Add(new WoodlandMansionPiece("small_wall_corner", Relative(Relative(position, east, 8), south, 7), rotation.GetRotated(StructureRotation.Clockwise90)));
                    }

                    if (!IsHouse(plan, x - 1, y))
                    {
                        if (!IsHouse(plan, x, y - 1))
                            pieces.Add(new WoodlandMansionPiece("small_wall_corner", Relative(Relative(position, west, 2), north, 1), rotation.GetRotated(StructureRotation.CounterClockwise90)));

                        if (!IsHouse(plan, x, y + 1))
                            pieces.Add(new WoodlandMansionPiece("small_wall_corner", Relative(Relative(position, west, 1), south, 8), rotation.GetRotated(StructureRotation.Clockwise180)));
                    }
                }
            }
        }

        for (var y = 0; y < plan.Height; y++)
        {
            for (var x = 0; x < plan.Width; x++)
            {
                var position = Relative(Relative(origin, south, 8 + (y - this.startY) * 8), east, (x - this.startX) * 8);
                var covered = above is not null && IsHouse(above, x, y);
                if (!IsHouse(plan, x, y) || covered)
                    continue;

                if (!IsHouse(plan, x + 1, y))
                {
                    var corner = Relative(position, east, 6);
                    if (!IsHouse(plan, x, y + 1))
                        pieces.Add(new WoodlandMansionPiece("roof_corner", Relative(corner, south, 6), rotation));
                    else if (IsHouse(plan, x + 1, y + 1))
                        pieces.Add(new WoodlandMansionPiece("roof_inner_corner", Relative(corner, south, 5), rotation));

                    if (!IsHouse(plan, x, y - 1))
                        pieces.Add(new WoodlandMansionPiece("roof_corner", corner, rotation.GetRotated(StructureRotation.CounterClockwise90)));
                    else if (IsHouse(plan, x + 1, y - 1))
                        pieces.Add(new WoodlandMansionPiece("roof_inner_corner", Relative(Relative(position, east, 9), north, 2), rotation.GetRotated(StructureRotation.Clockwise90)));
                }

                if (!IsHouse(plan, x - 1, y))
                {
                    var corner = Relative(Relative(position, east, 0), south, 0);
                    if (!IsHouse(plan, x, y + 1))
                        pieces.Add(new WoodlandMansionPiece("roof_corner", Relative(corner, south, 6), rotation.GetRotated(StructureRotation.Clockwise90)));
                    else if (IsHouse(plan, x - 1, y + 1))
                        pieces.Add(new WoodlandMansionPiece("roof_inner_corner", Relative(Relative(corner, south, 8), west, 3), rotation.GetRotated(StructureRotation.CounterClockwise90)));

                    if (!IsHouse(plan, x, y - 1))
                        pieces.Add(new WoodlandMansionPiece("roof_corner", corner, rotation.GetRotated(StructureRotation.Clockwise180)));
                    else if (IsHouse(plan, x - 1, y - 1))
                        pieces.Add(new WoodlandMansionPiece("roof_inner_corner", Relative(corner, south, 1), rotation.GetRotated(StructureRotation.Clockwise180)));
                }
            }
        }
    }

    private static void Entrance(List<WoodlandMansionPiece> pieces, PlacementData data)
    {
        pieces.Add(new WoodlandMansionPiece("entrance", Relative(data.Position, data.Rotation.Rotate(BlockFace.West), 9), data.Rotation));
        data.Position = Relative(data.Position, data.Rotation.Rotate(BlockFace.South), 16);
    }

    private static void TraverseWallPiece(List<WoodlandMansionPiece> pieces, PlacementData data)
    {
        pieces.Add(new WoodlandMansionPiece(data.WallType, Relative(data.Position, data.Rotation.Rotate(BlockFace.East), 7), data.Rotation));
        data.Position = Relative(data.Position, data.Rotation.Rotate(BlockFace.South), 8);
    }

    private static void TraverseTurn(List<WoodlandMansionPiece> pieces, PlacementData data)
    {
        data.Position = Relative(data.Position, data.Rotation.Rotate(BlockFace.South), -1);
        pieces.Add(new WoodlandMansionPiece("wall_corner", data.Position, data.Rotation));
        data.Position = Relative(data.Position, data.Rotation.Rotate(BlockFace.South), -7);
        data.Position = Relative(data.Position, data.Rotation.Rotate(BlockFace.West), -6);
        data.Rotation = data.Rotation.GetRotated(StructureRotation.Clockwise90);
    }

    private static void TraverseInnerTurn(PlacementData data)
    {
        data.Position = Relative(data.Position, data.Rotation.Rotate(BlockFace.South), 6);
        data.Position = Relative(data.Position, data.Rotation.Rotate(BlockFace.East), 8);
        data.Rotation = data.Rotation.GetRotated(StructureRotation.CounterClockwise90);
    }

    private void AddRoom1x1(List<WoodlandMansionPiece> pieces, Vector position, StructureRotation rotation, BlockFace? door, RoomCollection rooms)
    {
        var roomRotation = StructureRotation.None;
        var name = rooms.Get1x1(this.random);
        if (door != BlockFace.East)
        {
            if (door == BlockFace.North)
                roomRotation = roomRotation.GetRotated(StructureRotation.CounterClockwise90);
            else if (door == BlockFace.West)
                roomRotation = roomRotation.GetRotated(StructureRotation.Clockwise180);
            else if (door == BlockFace.South)
                roomRotation = roomRotation.GetRotated(StructureRotation.Clockwise90);
            else
                name = RoomCollection.Get1x1Secret(this.random);
        }

        var offset = StructureTemplate.GetZeroPositionWithTransform(new Vector(1, 0, 0), StructureMirror.None, roomRotation, 7, 7);
        roomRotation = roomRotation.GetRotated(rotation);
        offset = RotateVector(offset, rotation);
        pieces.Add(new WoodlandMansionPiece(name, position + (offset.X, 0, offset.Z), roomRotation));
    }

    private void AddRoom1x2(List<WoodlandMansionPiece> pieces, Vector position, StructureRotation rotation, BlockFace? side, BlockFace door,
        RoomCollection rooms, bool hasStairs)
    {
        var east = rotation.Rotate(BlockFace.East);
        var south = rotation.Rotate(BlockFace.South);

        switch (door, side)
        {
            case (BlockFace.East, BlockFace.South):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2SideEntrance(this.random, hasStairs), Relative(position, east, 1), rotation));
                break;
            case (BlockFace.East, BlockFace.North):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2SideEntrance(this.random, hasStairs), Relative(Relative(position, east, 1), south, 6), rotation,
                    StructureMirror.LeftRight));
                break;
            case (BlockFace.West, BlockFace.North):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2SideEntrance(this.random, hasStairs), Relative(Relative(position, east, 7), south, 6),
                    rotation.GetRotated(StructureRotation.Clockwise180)));
                break;
            case (BlockFace.West, BlockFace.South):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2SideEntrance(this.random, hasStairs), Relative(position, east, 7), rotation,
                    StructureMirror.FrontBack));
                break;
            case (BlockFace.South, BlockFace.East):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2SideEntrance(this.random, hasStairs), Relative(position, east, 1),
                    rotation.GetRotated(StructureRotation.Clockwise90), StructureMirror.LeftRight));
                break;
            case (BlockFace.South, BlockFace.West):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2SideEntrance(this.random, hasStairs), Relative(position, east, 7),
                    rotation.GetRotated(StructureRotation.Clockwise90)));
                break;
            case (BlockFace.North, BlockFace.West):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2SideEntrance(this.random, hasStairs), Relative(Relative(position, east, 7), south, 6),
                    rotation.GetRotated(StructureRotation.Clockwise90), StructureMirror.FrontBack));
                break;
            case (BlockFace.North, BlockFace.East):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2SideEntrance(this.random, hasStairs), Relative(Relative(position, east, 1), south, 6),
                    rotation.GetRotated(StructureRotation.CounterClockwise90)));
                break;
            case (BlockFace.South, BlockFace.North):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2FrontEntrance(this.random, hasStairs),
                    Relative(Relative(position, east, 1), rotation.Rotate(BlockFace.North), 8), rotation));
                break;
            case (BlockFace.North, BlockFace.South):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2FrontEntrance(this.random, hasStairs), Relative(Relative(position, east, 7), south, 14),
                    rotation.GetRotated(StructureRotation.Clockwise180)));
                break;
            case (BlockFace.West, BlockFace.East):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2FrontEntrance(this.random, hasStairs), Relative(position, east, 15),
                    rotation.GetRotated(StructureRotation.Clockwise90)));
                break;
            case (BlockFace.East, BlockFace.West):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2FrontEntrance(this.random, hasStairs),
                    Relative(Relative(position, rotation.Rotate(BlockFace.West), 7), south, 6), rotation.GetRotated(StructureRotation.CounterClockwise90)));
                break;
            case (BlockFace.Up, BlockFace.East):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2Secret(this.random), Relative(position, east, 15),
                    rotation.GetRotated(StructureRotation.Clockwise90)));
                break;
            case (BlockFace.Up, BlockFace.South):
                pieces.Add(new WoodlandMansionPiece(rooms.Get1x2Secret(this.random), Relative(Relative(position, east, 1), rotation.Rotate(BlockFace.North), 0),
                    rotation));
                break;
        }
    }

    private void AddRoom2x2(List<WoodlandMansionPiece> pieces, Vector position, StructureRotation rotation, BlockFace side, BlockFace door,
        RoomCollection rooms)
    {
        var east = 0;
        var south = 0;
        var roomRotation = rotation;
        var mirror = StructureMirror.None;

        switch (door, side)
        {
            case (BlockFace.East, BlockFace.South):
                east = -7;
                break;
            case (BlockFace.East, BlockFace.North):
                east = -7;
                south = 6;
                mirror = StructureMirror.LeftRight;
                break;
            case (BlockFace.North, BlockFace.East):
                east = 1;
                south = 14;
                roomRotation = rotation.GetRotated(StructureRotation.CounterClockwise90);
                break;
            case (BlockFace.North, BlockFace.West):
                east = 7;
                south = 14;
                roomRotation = rotation.GetRotated(StructureRotation.CounterClockwise90);
                mirror = StructureMirror.LeftRight;
                break;
            case (BlockFace.South, BlockFace.West):
                east = 7;
                south = -8;
                roomRotation = rotation.GetRotated(StructureRotation.Clockwise90);
                break;
            case (BlockFace.South, BlockFace.East):
                east = 1;
                south = -8;
                roomRotation = rotation.GetRotated(StructureRotation.Clockwise90);
                mirror = StructureMirror.LeftRight;
                break;
            case (BlockFace.West, BlockFace.North):
                east = 15;
                south = 6;
                roomRotation = rotation.GetRotated(StructureRotation.Clockwise180);
                break;
            case (BlockFace.West, BlockFace.South):
                east = 15;
                mirror = StructureMirror.FrontBack;
                break;
        }

        var roomPosition = Relative(Relative(position, rotation.Rotate(BlockFace.East), east), rotation.Rotate(BlockFace.South), south);
        pieces.Add(new WoodlandMansionPiece(rooms.Get2x2(this.random), roomPosition, roomRotation, mirror));
    }

    private static Vector Relative(Vector position, BlockFace direction, int distance) => position + direction.ToVector() * distance;

    /// <summary>Vanilla <c>BlockPos.rotate</c>: turns the vector around the origin.</summary>
    private static Vector RotateVector(Vector vector, StructureRotation rotation) => rotation switch
    {
        StructureRotation.Clockwise90 => new Vector(-vector.Z, vector.Y, vector.X),
        StructureRotation.Clockwise180 => new Vector(-vector.X, vector.Y, -vector.Z),
        StructureRotation.CounterClockwise90 => new Vector(vector.Z, vector.Y, -vector.X),
        _ => vector
    };

    /// <summary>A grid of cells with a value for cells outside it, like vanilla's <c>SimpleGrid</c>.</summary>
    private sealed class Grid(int width, int height, int valueIfOutside)
    {
        // Row-major: cell (x, y) is at y * Width + x.
        private readonly int[] cells = new int[width * height];

        public int Width { get; } = width;

        public int Height { get; } = height;

        public void Set(int x, int y, int value)
        {
            if (x >= 0 && x < this.Width && y >= 0 && y < this.Height)
                this.cells[y * this.Width + x] = value;
        }

        public void Set(int minX, int minY, int maxX, int maxY, int value)
        {
            for (var y = minY; y <= maxY; y++)
            {
                for (var x = minX; x <= maxX; x++)
                    this.Set(x, y, value);
            }
        }

        public int Get(int x, int y) => x >= 0 && x < this.Width && y >= 0 && y < this.Height ? this.cells[y * this.Width + x] : valueIfOutside;

        public void SetIf(int x, int y, int expected, int value)
        {
            if (this.Get(x, y) == expected)
                this.Set(x, y, value);
        }

        /// <summary>Whether a horizontal neighbor of the cell has <paramref name="value"/>.</summary>
        public bool EdgesTo(int x, int y, int value) =>
            this.Get(x - 1, y) == value || this.Get(x + 1, y) == value || this.Get(x, y + 1) == value || this.Get(x, y - 1) == value;
    }

    private sealed class PlacementData
    {
        public required StructureRotation Rotation { get; set; }

        public required Vector Position { get; set; }

        public required string WallType { get; init; }
    }

    /// <summary>The room templates of a floor, like vanilla's <c>FloorRoomCollection</c>s.</summary>
    private sealed class RoomCollection
    {
        public static RoomCollection FirstFloor { get; } = new(true);

        /// <summary>The second floor's rooms, which the third floor uses too.</summary>
        public static RoomCollection SecondFloor { get; } = new(false);

        private readonly bool firstFloor;

        private RoomCollection(bool firstFloor) => this.firstFloor = firstFloor;

        public string Get1x1(IRandomSource random) => (this.firstFloor ? "1x1_a" : "1x1_b") + (random.NextInt(5) + 1);

        // The secret rooms are the same on every floor.
        public static string Get1x1Secret(IRandomSource random) => "1x1_as" + (random.NextInt(4) + 1);

        public string Get1x2SideEntrance(IRandomSource random, bool hasStairs)
        {
            if (this.firstFloor)
                return "1x2_a" + (random.NextInt(9) + 1);

            return hasStairs ? "1x2_c_stairs" : "1x2_c" + (random.NextInt(4) + 1);
        }

        public string Get1x2FrontEntrance(IRandomSource random, bool hasStairs)
        {
            if (this.firstFloor)
                return "1x2_b" + (random.NextInt(5) + 1);

            return hasStairs ? "1x2_d_stairs" : "1x2_d" + (random.NextInt(5) + 1);
        }

        public string Get1x2Secret(IRandomSource random) => this.firstFloor ? "1x2_s" + (random.NextInt(2) + 1) : "1x2_se" + (random.NextInt(1) + 1);

        public string Get2x2(IRandomSource random) => this.firstFloor ? "2x2_a" + (random.NextInt(4) + 1) : "2x2_b" + (random.NextInt(5) + 1);

        public static string Get2x2Secret() => "2x2_s1";
    }
}
