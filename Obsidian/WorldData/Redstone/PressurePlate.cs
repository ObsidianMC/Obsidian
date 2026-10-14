using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.Entities;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A pressure plate, like vanilla's <c>PressurePlateBlock</c> and <c>WeightedPressurePlateBlock</c>: entities on it
/// turn it on, and it checks again some ticks later whether they're still there. Wooden plates feel any entity,
/// stone ones only mobs and players; weighted plates give more power the more entities are on them.
/// </summary>
/// <param name="mobsOnly">Vanilla's <c>PressurePlateSensitivity.MOBS</c>.</param>
/// <param name="maxWeight">For a weighted plate, the entities that give full power; 0 for others.</param>
/// <param name="sound">The prefix of its click sounds.</param>
internal sealed class PressurePlate(bool mobsOnly, int maxWeight, string sound) : RedstoneComponent
{
    // Vanilla's TOUCH_AABB: the plate's area, a quarter block high, that entities press it in.
    private static readonly VectorD touchMin = new(1 / 16.0, 0, 1 / 16.0);
    private static readonly VectorD touchMax = new(15 / 16.0, 4 / 16.0, 15 / 16.0);

    private bool IsWeighted => maxWeight > 0;

    // Vanilla's getPressedTime.
    private int PressedTime => this.IsWeighted ? 10 : 20;

    /// <summary>
    /// The plain plate with the given identifier, by vanilla's block set types.
    /// </summary>
    public static PressurePlate For(string name) => name switch
    {
        "minecraft:stone_pressure_plate" or "minecraft:polished_blackstone_pressure_plate" =>
            new(mobsOnly: true, maxWeight: 0, "stone_pressure_plate"),
        _ => new(mobsOnly: false, maxWeight: 0, Button.WoodSound(name, "pressure_plate"))
    };

    /// <summary>
    /// The weighted plate with the given identifier: 15 entities give the light (gold) one full power, 150 the
    /// heavy (iron) one.
    /// </summary>
    public static PressurePlate Weighted(string name) =>
        new(mobsOnly: false, name == "minecraft:light_weighted_pressure_plate" ? 15 : 150, "metal_pressure_plate");

    public override bool IsSignalSource(LevelBlockUpdates level) => true;

    public override int GetSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        this.SignalOf(state);

    // It strongly powers the block it's on.
    public override int GetDirectSignal(LevelBlockUpdates level, Vector position, IBlock state, BlockFace direction) =>
        direction == BlockFace.Up ? this.SignalOf(state) : 0;

    public override IBlock? GetStateForPlacement(RedstonePlacement placement) =>
        CanSurvive(placement.Level, placement.Position) ? placement.Block : null;

    public override void OnRemoved(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (this.SignalOf(state) > 0)
            UpdateNeighbours(level, position, state);
    }

    public override IBlock UpdateShape(
        LevelBlockUpdates level,
        Vector position,
        IBlock state,
        BlockFace direction,
        IBlock neighbor) =>
        direction == BlockFace.Down && !CanSurvive(level, position) ? BlocksRegistry.Air : state;

    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var signal = this.SignalOf(state);
        if (signal > 0)
            this.CheckPressed(level, position, state, signal);
    }

    public override void EntityInside(LevelBlockUpdates level, Vector position, IBlock state, IEntity entity)
    {
        var signal = this.SignalOf(state);
        if (signal == 0)
            this.CheckPressed(level, position, state, signal);
    }

    // Vanilla canSurvive: a rigid or centre-sturdy top below.
    private static bool CanSurvive(LevelBlockUpdates level, Vector position)
    {
        var below = level.GetBlock(position + Vector.Down);
        return below.IsTopRigidSturdy() || below.IsTopCenterSturdy();
    }

    // Vanilla's updateNeighbours: its own neighbours, then those of the block it's on.
    private static void UpdateNeighbours(LevelBlockUpdates level, Vector position, IBlock state)
    {
        level.UpdateNeighbors(position, state);
        level.UpdateNeighbors(position + Vector.Down, state);
    }

    // Vanilla checkPressed: takes the power the entities on it give, clicks when it turns on or off, and checks
    // again while on.
    private void CheckPressed(LevelBlockUpdates level, Vector position, IBlock state, int current)
    {
        var signal = this.SignalStrength(level, position);
        if (signal != current)
        {
            var updated = this.WithSignal(state, signal);
            level.Set(position, updated, BlockUpdateFlags.Clients);
            UpdateNeighbours(level, position, updated);
        }

        if (signal == 0 && current > 0)
            level.PlaySound(position, $"minecraft:block.{sound}.click_off");
        else if (signal > 0 && current == 0)
            level.PlaySound(position, $"minecraft:block.{sound}.click_on");

        if (signal > 0)
            level.ScheduleTick(position, level.GetBlock(position), this.PressedTime);
    }

    // Vanilla getSignalStrength: full power with any entity it feels on it, or for a weighted plate the share of
    // its maximum weight, rounded up.
    private int SignalStrength(LevelBlockUpdates level, Vector position)
    {
        var corner = (VectorD)position;
        var count = level.EntitiesIn(corner + touchMin, corner + touchMax).Count(this.Presses);

        if (!this.IsWeighted)
            return count > 0 ? 15 : 0;

        var weight = Math.Min(count, maxWeight);
        return weight > 0 ? (int)MathF.Ceiling(weight / (float)maxWeight * 15f) : 0;
    }

    // Vanilla's entity selector: not spectators, and only living entities for stone plates.
    private bool Presses(IEntity entity)
    {
        if (entity is IPlayer { GameMode: GameMode.Spectator })
            return false;

        return !mobsOnly || entity is Living;
    }

    private int SignalOf(IBlock state) => this.IsWeighted
        ? int.Parse(state.GetProperty("power")!)
        : state.GetProperty("powered") == "true" ? 15 : 0;

    private IBlock WithSignal(IBlock state, int signal) => this.IsWeighted
        ? state.WithProperty("power", signal)
        : state.WithProperty("powered", signal > 0);
}
