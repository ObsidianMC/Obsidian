using Obsidian.API.Blocks;
using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A rail and the neighbours it joins, like vanilla's <c>RailState</c>: placing a rail turns it towards the rails
/// around it that can still take a connection, and turns them back towards it.
/// </summary>
internal sealed class RailConnections
{
    private static readonly Vector North = new(0, 0, -1);
    private static readonly Vector South = new(0, 0, 1);
    private static readonly Vector West = new(-1, 0, 0);
    private static readonly Vector East = new(1, 0, 0);

    private readonly IRedstoneLevel level;
    private readonly Vector position;
    private readonly bool straight;
    private readonly List<Vector> connections = [];

    public RailConnections(IRedstoneLevel level, Vector position, IBlock state)
    {
        this.level = level;
        this.position = position;
        this.State = state;
        this.straight = Rails.IsStraight(state);
        this.UpdateConnections(Rails.ShapeOf(state));
    }

    /// <summary>The rail's state, as placed.</summary>
    public IBlock State { get; private set; }

    /// <summary>Where the rail's two ends lead.</summary>
    public IReadOnlyList<Vector> Connections => this.connections;

    private void UpdateConnections(RailShape shape)
    {
        this.connections.Clear();
        var (first, second) = shape switch
        {
            RailShape.NorthSouth => (North, South),
            RailShape.EastWest => (West, East),
            RailShape.AscendingEast => (West, East + Vector.Up),
            RailShape.AscendingWest => (West + Vector.Up, East),
            RailShape.AscendingNorth => (North + Vector.Up, South),
            RailShape.AscendingSouth => (North, South + Vector.Up),
            RailShape.SouthEast => (East, South),
            RailShape.SouthWest => (West, South),
            RailShape.NorthWest => (West, North),
            _ => (East, North)
        };

        this.connections.Add(this.position + first);
        this.connections.Add(this.position + second);
    }

    // Vanilla's removeSoftConnections: only the ends whose rails lead back here stay.
    private void RemoveSoftConnections()
    {
        for (var index = 0; index < this.connections.Count; index++)
        {
            var rail = this.GetRail(this.connections[index]);
            if (rail is null || !rail.ConnectsTo(this))
                this.connections.RemoveAt(index--);
            else
                this.connections[index] = rail.position;
        }
    }

    // Vanilla's getBlockState: an unloaded position reads as air, so no rail is there.
    private IBlock GetBlock(Vector at) => this.level.GetLoadedBlock(at) ?? BlocksRegistry.Air;

    private bool IsRail(Vector at) => Rails.IsRail(this.GetBlock(at));

    private bool HasRail(Vector at) => this.IsRail(at) || this.IsRail(at + Vector.Up) || this.IsRail(at + Vector.Down);

    // Vanilla's getRail: the rail at a neighbour, or a block above or below it.
    private RailConnections? GetRail(Vector at)
    {
        foreach (var candidate in new[] { at, at + Vector.Up, at + Vector.Down })
        {
            var state = this.GetBlock(candidate);
            if (Rails.IsRail(state))
                return new RailConnections(this.level, candidate, state);
        }

        return null;
    }

    private bool ConnectsTo(RailConnections other) => this.HasConnection(other.position);

    private bool HasConnection(Vector at) =>
        this.connections.Any(connection => connection.X == at.X && connection.Z == at.Z);

    /// <summary>
    /// Vanilla's <c>countPotentialConnections</c>: how many sides have a rail beside, above or below.
    /// </summary>
    public int CountPotentialConnections() =>
        new[] { North, South, West, East }.Count(direction => this.HasRail(this.position + direction));

    private bool CanConnectTo(RailConnections other) => this.ConnectsTo(other) || this.connections.Count != 2;

    // Vanilla's connectTo: a neighbour turns towards a newly placed rail.
    private async ValueTask ConnectToAsync(RailConnections other)
    {
        this.connections.Add(other.position);
        var north = this.HasConnection(this.position + North);
        var south = this.HasConnection(this.position + South);
        var west = this.HasConnection(this.position + West);
        var east = this.HasConnection(this.position + East);

        RailShape? shape = null;
        if (north || south)
            shape = RailShape.NorthSouth;
        if (west || east)
            shape = RailShape.EastWest;

        if (!this.straight)
        {
            if (south && east && !north && !west)
                shape = RailShape.SouthEast;
            if (south && west && !north && !east)
                shape = RailShape.SouthWest;
            if (north && west && !south && !east)
                shape = RailShape.NorthWest;
            if (north && east && !south && !west)
                shape = RailShape.NorthEast;
        }

        shape = this.Raised(shape) ?? RailShape.NorthSouth;
        this.State = Rails.WithShape(this.State, shape.Value);
        await this.level.SetBlockAsync(this.position, this.State);
    }

    // A straight rail runs up towards a rail one block higher.
    private RailShape? Raised(RailShape? shape)
    {
        if (shape == RailShape.NorthSouth)
        {
            if (this.IsRail(this.position + North + Vector.Up))
                shape = RailShape.AscendingNorth;
            if (this.IsRail(this.position + South + Vector.Up))
                shape = RailShape.AscendingSouth;
        }

        if (shape == RailShape.EastWest)
        {
            if (this.IsRail(this.position + East + Vector.Up))
                shape = RailShape.AscendingEast;
            if (this.IsRail(this.position + West + Vector.Up))
                shape = RailShape.AscendingWest;
        }

        return shape;
    }

    private bool HasNeighborRail(Vector at)
    {
        if (this.GetRail(at) is not RailConnections rail)
            return false;

        rail.RemoveSoftConnections();
        return rail.CanConnectTo(this);
    }

    /// <summary>
    /// Vanilla's <c>place</c>: the rail turns towards the neighbours that can take it, curving the way power picks
    /// at a junction, and when it changed (or always, as it's placed) the neighbours turn towards it.
    /// </summary>
    public async ValueTask<RailConnections> PlaceAsync(bool powered, bool alwaysPlace, RailShape current)
    {
        var north = this.HasNeighborRail(this.position + North);
        var south = this.HasNeighborRail(this.position + South);
        var west = this.HasNeighborRail(this.position + West);
        var east = this.HasNeighborRail(this.position + East);

        RailShape? shape = null;
        var northSouth = north || south;
        var eastWest = west || east;
        if (northSouth && !eastWest)
            shape = RailShape.NorthSouth;
        if (eastWest && !northSouth)
            shape = RailShape.EastWest;

        var southEast = south && east;
        var southWest = south && west;
        var northEast = north && east;
        var northWest = north && west;
        if (!this.straight)
        {
            if (southEast && !north && !west)
                shape = RailShape.SouthEast;
            if (southWest && !north && !east)
                shape = RailShape.SouthWest;
            if (northWest && !south && !east)
                shape = RailShape.NorthWest;
            if (northEast && !south && !west)
                shape = RailShape.NorthEast;
        }

        if (shape is null)
        {
            if (northSouth && eastWest)
                shape = current;
            else if (northSouth)
                shape = RailShape.NorthSouth;
            else if (eastWest)
                shape = RailShape.EastWest;

            if (!this.straight)
            {
                if (powered)
                {
                    if (southEast)
                        shape = RailShape.SouthEast;
                    if (southWest)
                        shape = RailShape.SouthWest;
                    if (northEast)
                        shape = RailShape.NorthEast;
                    if (northWest)
                        shape = RailShape.NorthWest;
                }
                else
                {
                    if (northWest)
                        shape = RailShape.NorthWest;
                    if (northEast)
                        shape = RailShape.NorthEast;
                    if (southWest)
                        shape = RailShape.SouthWest;
                    if (southEast)
                        shape = RailShape.SouthEast;
                }
            }
        }

        var placed = this.Raised(shape) ?? current;
        this.UpdateConnections(placed);
        this.State = Rails.WithShape(this.State, placed);

        if (alwaysPlace || !this.GetBlock(this.position).IsSameState(this.State))
        {
            await this.level.SetBlockAsync(this.position, this.State);
            foreach (var connection in this.connections.ToArray())
            {
                if (this.GetRail(connection) is not RailConnections rail)
                    continue;

                rail.RemoveSoftConnections();
                if (rail.CanConnectTo(this))
                    await rail.ConnectToAsync(this);
            }
        }

        return this;
    }
}
