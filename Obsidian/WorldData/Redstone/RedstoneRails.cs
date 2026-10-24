using Obsidian.API.Blocks;
using Obsidian.Entities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The plain rail's block, like vanilla's <c>BaseRailBlock</c>: placed along the way the player faces, it turns to join
/// the rails around it, which turn to join it (see <see cref="RailConnections"/>), and a plain rail at a T-junction
/// switches its curve when a redstone source beside it changes. An unsupported rail drops.
/// </summary>
internal class RedstoneRail(IRedstoneLevel level, RedstoneEngine engine) : INeighborChangedBehavior, IPlacementBehavior, IRemovalBehavior
{
    /// <summary>
    /// Vanilla's <c>BaseRailBlock.shouldBeRemoved</c>: nothing rigid under the rail, or under a slope's raised end. An
    /// unloaded block reads as supporting, so the rail is kept until its chunk is back.
    /// </summary>
    public static bool IsUnsupported(Vector position, IBlock state, Func<Vector, IBlock?> blockAt)
    {
        if (blockAt(position + Vector.Down) is IBlock below && !below.IsFaceSturdy(BlockFace.Up, SupportType.Rigid))
            return true;

        if (state.GetProperty("shape") is string shape && shape.StartsWith("ascending_", StringComparison.Ordinal)
            && blockAt(position.Offset(FeatureHelpers.ParseFace(shape[10..]))) is IBlock raised)
        {
            return !raised.IsFaceSturdy(BlockFace.Up, SupportType.Rigid);
        }

        return false;
    }

    /// <summary>Vanilla's <c>getStateForPlacement</c>: along the player's facing, waterlogged in a water source.</summary>
    public static IBlock PlacementState(BlockPlaceContext context)
    {
        var there = context.Level.GetBlock(context.Position);
        var eastWest = context.HorizontalDirection is BlockFace.East or BlockFace.West;
        var state = Rails.WithShape(context.Block, eastWest ? RailShape.EastWest : RailShape.NorthSouth);

        return state.WithProperty("waterlogged", there.Is(BlockIds.Water) && there.GetProperty("level") is null or "0");
    }

    protected IRedstoneLevel Level { get; } = level;

    protected RedstoneEngine Engine { get; } = engine;

    public async ValueTask OnNeighborChangedAsync(NeighborChange change, IBlockChangeWorld world)
    {
        // Vanilla's neighborChanged: an unsupported rail drops, with no break effect; one that stays reacts.
        if (IsUnsupported(change.Position, change.State, this.Level.GetLoadedBlock))
        {
            this.Level.DropAndRemove(change.Position, change.State, playEffect: false);
            return;
        }

        await this.ReactAsync(change.Position, change.State, change.SourceState);
    }

    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld world) => this.PlacedAsync(change.Position, change.New);

    // Vanilla's onPlace: a new rail joins up.
    protected virtual ValueTask PlacedAsync(Vector position, IBlock state) => this.UpdateStateAsync(position, state);

    // Vanilla's affectNeighborsAfterRemoval: a slope's top neighbours hear of it going, and a straight rail's neighbours
    // and those of the block below.
    public async ValueTask OnRemovedAsync(BlockChange change, IBlockChangeWorld world)
    {
        var state = change.Old;
        if (Rails.IsSlope(Rails.ShapeOf(state)))
            await this.Level.NotifyNeighborsAsync(change.Position + Vector.Up);

        if (Rails.IsStraight(state))
        {
            await this.Level.NotifyNeighborsAsync(change.Position);
            await this.Level.NotifyNeighborsAsync(change.Position + Vector.Down);
        }
    }

    /// <summary>
    /// Vanilla's <c>neighborChanged(state, level, pos, block)</c>: the plain rail's T-junction switch.
    /// </summary>
    protected virtual async ValueTask ReactAsync(Vector position, IBlock state, IBlock? source)
    {
        if (source is not null
            && BlocksRegistry.GetDefault(source.RegistryId).IsSignalSource()
            && new RailConnections(this.Level, position, state).CountPotentialConnections() == 3)
        {
            await this.UpdateDirectionAsync(position, state, alwaysPlace: false);
        }
    }

    /// <summary>
    /// Vanilla's <c>updateState</c>: the rail turns to join its neighbours, and a straight rail checks its power.
    /// </summary>
    protected async ValueTask UpdateStateAsync(Vector position, IBlock state)
    {
        state = await this.UpdateDirectionAsync(position, state, alwaysPlace: true);
        if (Rails.IsStraight(state))
            await this.ReactAsync(position, state, state);
    }

    // Vanilla's updateDir.
    protected async ValueTask<IBlock> UpdateDirectionAsync(Vector position, IBlock state, bool alwaysPlace) =>
        (await new RailConnections(this.Level, position, state)
            .PlaceAsync(this.Engine.HasNeighborSignal(position), alwaysPlace, Rails.ShapeOf(state))).State;
}

/// <summary>
/// The powered and activator rails' block, like vanilla's <c>PoweredRailBlock</c>: powered while a neighbour powers
/// them, or a powered rail of the same kind leads to them within eight rails.
/// </summary>
internal sealed class RedstonePoweredRail(IRedstoneLevel level, RedstoneEngine engine) : RedstoneRail(level, engine)
{
    // Vanilla's: power travels along at most this many rails.
    private const int MaxDistance = 8;

    protected override async ValueTask ReactAsync(Vector position, IBlock state, IBlock? source)
    {
        var powered = Rails.IsPowered(state);
        var shouldPower = this.Engine.HasNeighborSignal(position)
            || this.FindPoweredRailSignal(position, state, forward: true, 0)
            || this.FindPoweredRailSignal(position, state, forward: false, 0);

        if (shouldPower == powered)
            return;

        var changed = state.WithProperty("powered", shouldPower);
        await this.Level.SetBlockAsync(position, changed);
        await this.Level.NotifyNeighborsAsync(position + Vector.Down);
        if (Rails.IsSlope(Rails.ShapeOf(state)))
            await this.Level.NotifyNeighborsAsync(position + Vector.Up);
    }

    // Vanilla's findPoweredRailSignal: the next rail along, or the one under it where the line can step down.
    private bool FindPoweredRailSignal(Vector position, IBlock state, bool forward, int distance)
    {
        if (distance >= MaxDistance)
            return false;

        var (x, y, z) = (position.X, position.Y, position.Z);
        var stepDown = true;
        var shape = Rails.ShapeOf(state);
        switch (shape)
        {
            case RailShape.NorthSouth:
                z += forward ? 1 : -1;
                break;
            case RailShape.EastWest:
                x += forward ? -1 : 1;
                break;
            case RailShape.AscendingEast:
                if (forward)
                {
                    x--;
                }
                else
                {
                    x++;
                    y++;
                    stepDown = false;
                }

                shape = RailShape.EastWest;
                break;
            case RailShape.AscendingWest:
                if (forward)
                {
                    x--;
                    y++;
                    stepDown = false;
                }
                else
                {
                    x++;
                }

                shape = RailShape.EastWest;
                break;
            case RailShape.AscendingNorth:
                if (forward)
                {
                    z++;
                }
                else
                {
                    z--;
                    y++;
                    stepDown = false;
                }

                shape = RailShape.NorthSouth;
                break;
            case RailShape.AscendingSouth:
                if (forward)
                {
                    z++;
                    y++;
                    stepDown = false;
                }
                else
                {
                    z--;
                }

                shape = RailShape.NorthSouth;
                break;
        }

        return this.IsSameRailWithPower(new Vector(x, y, z), state, forward, distance, shape)
            || stepDown && this.IsSameRailWithPower(new Vector(x, y - 1, z), state, forward, distance, shape);
    }

    // Vanilla's isSameRailWithPower: a powered rail of the same kind, in line, that's powered itself or leads on to
    // power.
    private bool IsSameRailWithPower(Vector position, IBlock kind, bool forward, int distance, RailShape shape)
    {
        var state = this.Level.GetLoadedBlock(position) ?? BlocksRegistry.Air;
        if (state.RegistryId != kind.RegistryId)
            return false;

        var railShape = Rails.ShapeOf(state);
        if (shape == RailShape.EastWest
            && railShape is RailShape.NorthSouth or RailShape.AscendingNorth or RailShape.AscendingSouth)
        {
            return false;
        }

        if (shape == RailShape.NorthSouth
            && railShape is RailShape.EastWest or RailShape.AscendingEast or RailShape.AscendingWest)
        {
            return false;
        }

        return Rails.IsPowered(state)
            && (this.Engine.HasNeighborSignal(position)
                || this.FindPoweredRailSignal(position, state, forward, distance + 1));
    }
}

/// <summary>
/// The detector rail's block, like vanilla's <c>DetectorRailBlock</c>: a minecart on it powers it, strongly to the
/// block below, until a check 20 ticks after the last minecart left; comparators read how full a container minecart is,
/// or a command block minecart's successes.
/// </summary>
internal sealed class RedstoneDetectorRail : RedstoneRail, IRedstoneContact
{
    public const int BlockId = BlockIds.DetectorRail;

    // Vanilla's: the rail checks again this often while pressed.
    private const int PressedCheckDelay = 20;

    private readonly RedstoneDetectorPower power = new();

    private RedstoneDetectorRail(IRedstoneLevel level, RedstoneEngine engine) : base(level, engine)
    {
    }

    public static void Register(IRedstoneLevel level, RedstoneEngine engine, LevelBlockTicks ticks, RedstoneBehaviors redstone, NeighborBehaviors neighbors)
    {
        var rail = new RedstoneDetectorRail(level, engine);
        neighbors.Register(BlockId, rail);
        neighbors.RegisterPlaced(BlockId, rail);
        neighbors.RegisterRemoved(BlockId, rail);
        redstone.Register(BlocksRegistry.GetDefault(BlockId).BlockClass(), rail.power);
        redstone.RegisterContact(BlocksRegistry.GetDefault(BlockId).BlockClass(), rail);
        ticks.Register(BlockId, (position, _) => level.EnqueueSimulation(() => rail.TickAsync(position)));
    }

    /// <summary>
    /// Vanilla's <c>getAnalogOutputSignal</c>: while pressed, a command block minecart's successes, or how full a
    /// container minecart is.
    /// </summary>
    public static int AnalogOutput(IBlock state, IEnumerable<IEntity> entitiesInBox)
    {
        if (!Rails.IsPowered(state))
            return 0;

        var carts = Minecarts(entitiesInBox).ToArray();
        if (carts.OfType<CommandBlockMinecart>().FirstOrDefault() is CommandBlockMinecart command)
            return command.SuccessCount;

        return carts.OfType<IVehicleContainer>().FirstOrDefault() is IVehicleContainer container
            ? container.RedstoneSignal
            : 0;
    }

    /// <summary>The box a minecart must overlap to press the rail at <paramref name="position"/>.</summary>
    public static (VectorD Min, VectorD Max) MinecartBox(Vector position) =>
        (new VectorD(position.X + 0.2, position.Y, position.Z + 0.2),
            new VectorD(position.X + 0.8, position.Y + 0.8, position.Z + 0.8));

    // A detector rail's own neighbour changes only matter for its support.
    protected override ValueTask ReactAsync(Vector position, IBlock state, IBlock? source) => ValueTask.CompletedTask;

    // Vanilla's onPlace: it joins up, then checks for a minecart already on it.
    protected override async ValueTask PlacedAsync(Vector position, IBlock state)
    {
        await this.UpdateStateAsync(position, state);
        if (this.Level.GetLoadedBlock(position) is IBlock placed && placed.Is(BlockId))
            await this.CheckPressedAsync(position, placed);
    }

    // Vanilla's entityInside: a minecart on the rail presses it.
    public async ValueTask EntityInsideAsync(IRedstoneLevel level, Vector position, IBlock state)
    {
        if (!Rails.IsPowered(state))
            await this.CheckPressedAsync(position, state);
    }

    // Vanilla's tick: while pressed, check again.
    public async ValueTask TickAsync(Vector position)
    {
        if (this.Level.GetLoadedBlock(position) is IBlock state && state.Is(BlockId) && Rails.IsPowered(state))
            await this.CheckPressedAsync(position, state);
    }

    // Vanilla's checkPressed.
    private async ValueTask CheckPressedAsync(Vector position, IBlock state)
    {
        if (IsUnsupported(position, state, this.Level.GetLoadedBlock))
            return;

        var powered = Rails.IsPowered(state);
        var pressed = this.Minecarts(position).Any();

        if (pressed != powered)
        {
            state = state.WithProperty("powered", pressed);
            await this.Level.SetBlockAsync(position, state);
            await this.UpdatePowerToConnectedAsync(position, state);
            await this.Level.NotifyNeighborsAsync(position);
            await this.Level.NotifyNeighborsAsync(position + Vector.Down);
        }

        if (pressed)
            this.Level.ScheduleBlockTick(position, state, PressedCheckDelay);

        await this.Level.NotifyComparatorsAroundAsync(position);
    }

    // Vanilla's updatePowerToConnected: the rails it joins hear of it.
    private async ValueTask UpdatePowerToConnectedAsync(Vector position, IBlock state)
    {
        foreach (var connection in new RailConnections(this.Level, position, state).Connections)
            await this.Level.NotifyNeighborAsync(connection, position);
    }

    private IEnumerable<AbstractMinecart> Minecarts(Vector position)
    {
        var (min, max) = MinecartBox(position);
        return Minecarts(this.Level.EntitiesIn(min, max));
    }

    private static IEnumerable<AbstractMinecart> Minecarts(IEnumerable<IEntity> entities) => entities
        .OfType<AbstractMinecart>()
        .Where(cart => !cart.IsRemoved);

    /// <summary>The detector rail's signal: 15 from it, and straight up.</summary>
    private sealed class RedstoneDetectorPower : IRedstoneBehavior
    {
        public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
            Rails.IsPowered(state) ? RedstoneEngine.MaxSignal : 0;

        public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
            Rails.IsPowered(state) && direction == BlockFace.Up ? RedstoneEngine.MaxSignal : 0;
    }
}

/// <summary>
/// Registers the rails' reactions: the plain, powered and activator rails' placement, neighbour and removal reactions,
/// and the detector rail's power, tick and entity contact.
/// </summary>
internal static class RedstoneRails
{
    public static void Register(IRedstoneLevel level, RedstoneEngine engine, LevelBlockTicks ticks, RedstoneBehaviors redstone, NeighborBehaviors neighbors)
    {
        var plain = new RedstoneRail(level, engine);
        neighbors.Register(BlockIds.Rail, plain);
        neighbors.RegisterPlaced(BlockIds.Rail, plain);
        neighbors.RegisterRemoved(BlockIds.Rail, plain);

        // Vanilla's activator rails are powered rails: PoweredRailBlock covers both.
        var powered = new RedstonePoweredRail(level, engine);
        foreach (var id in new[] { BlockIds.PoweredRail, BlockIds.ActivatorRail })
        {
            neighbors.Register(id, powered);
            neighbors.RegisterPlaced(id, powered);
            neighbors.RegisterRemoved(id, powered);
        }

        RedstoneDetectorRail.Register(level, engine, ticks, redstone, neighbors);
    }
}

/// <summary>
/// The placement of a rail, like vanilla's <c>BaseRailBlock.getStateForPlacement</c>.
/// </summary>
internal sealed class RailPlacement : IBlockBehavior
{
    public IBlock? GetStateForPlacement(BlockPlaceContext context) => RedstoneRail.PlacementState(context);
}
