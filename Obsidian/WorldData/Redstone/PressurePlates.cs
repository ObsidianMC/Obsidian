using Obsidian.Entities;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Pressure plates (vanilla <c>BasePressurePlateBlock</c>, <c>PressurePlateBlock</c> and
/// <c>WeightedPressurePlateBlock</c>). An entity inside an unpressed plate has it count the entities on it; a pressed
/// plate counts them again only on its scheduled check, 20 ticks later (10 for weighted plates), and stays pressed while
/// any are there.
/// </summary>
/// <remarks>
/// The entities on a plate are those whose boxes overlap its touch box, the bottom quarter of the block inset by 1/16 on
/// each side. Stone and polished blackstone plates count living entities only (vanilla's <c>MOBS</c> sensitivity:
/// players, mobs and armor stands); the others count every entity. Spectators and entities that ignore block triggers
/// (markers, displays, interactions) never count. Who presses a plate is decided by the entity contact pass
/// (<see cref="EntityContactPass"/>), which runs <see cref="EntityInsideAsync"/> for each entity straight after its tick,
/// so an entity standing on an unpressed plate presses it in its own tick, as in vanilla.
/// </remarks>
internal sealed class PressurePlates(IRedstoneLevel level)
{
    // Vanilla BasePressurePlateBlock.getPressedTime and WeightedPressurePlateBlock.getPressedTime.
    private const int PlateDelay = 20;
    private const int WeightedDelay = 10;

    // Vanilla's TOUCH_AABB: 14/16 across, centred, and 4/16 high.
    private const double TouchInset = 1d / 16d;
    private const double TouchHeight = 4d / 16d;

    // The generated block tag of every pressure plate (vanilla #minecraft:pressure_plates), the set this engine drives.
    private static readonly ImmutableArray<int> plates = TagsRegistry.Block.PressurePlates.Entries;

    public static bool IsPlate(IBlock state) => plates.Contains(state.RegistryId);

    private static bool IsWeighted(IBlock state) => state.BlockClass() == "WeightedPressurePlateBlock";

    // Vanilla's plate clicks: weighted plates use the metal sounds, the others their block set's.
    private static string PlateSound(IBlock state, bool on) =>
        $"minecraft:block.{(IsWeighted(state) ? "metal" : VanillaPlacement.SoundMaterial(state))}_pressure_plate.{(on ? "click_on" : "click_off")}";

    // Vanilla PressurePlateSensitivity: stone and polished blackstone plates use MOBS; the other plates use EVERYTHING.
    private static bool CountsLivingOnly(IBlock state) =>
        TagsRegistry.Block.StonePressurePlates.Entries.Contains(state.RegistryId);

    private static int Delay(IBlock state) => IsWeighted(state) ? WeightedDelay : PlateDelay;

    private static int MaxWeight(IBlock state) => state.Is(BlockIds.HeavyWeightedPressurePlate) ? 150 : 15;

    /// <summary>
    /// Vanilla <c>WeightedPressurePlateBlock.getSignalStrength</c>: the entity count, up to the plate's maximum weight,
    /// scaled to 15 and rounded up. In floats, as vanilla works it out, since the rounding decides the power.
    /// </summary>
    internal static int WeightedStrength(int count, int maxWeight)
    {
        var clamped = Math.Min(count, maxWeight);
        if (clamped <= 0)
            return 0;

        var share = (float)clamped / maxWeight;
        return (int)MathF.Ceiling(share * RedstoneEngine.MaxSignal);
    }

    /// <summary>
    /// Registers the plates' scheduled checks. They run on the level's simulation path, after block ticks.
    /// </summary>
    public void RegisterTicks(LevelBlockTicks ticks)
    {
        foreach (var id in plates)
            ticks.Register(id, (position, _) => level.EnqueueSimulation(() => this.TickAsync(position)));
    }

    /// <summary>
    /// Vanilla <c>entityInside</c>: an entity inside an unpressed plate has it count the entities on it, itself among
    /// them if it overlaps the touch box. A pressed plate leaves the counting to its check.
    /// </summary>
    public ValueTask EntityInsideAsync(Vector position, IBlock state, Entity entity)
    {
        if (Signal(state) != 0)
            return ValueTask.CompletedTask;

        return this.CheckPressedAsync(position, state, entity);
    }

    /// <summary>Vanilla <c>tick</c>: a pressed plate counts the entities on it again.</summary>
    internal async ValueTask TickAsync(Vector position)
    {
        if (level.GetLoadedBlock(position) is not IBlock state || !IsPlate(state) || Signal(state) == 0)
            return;

        await this.CheckPressedAsync(position, state, source: null);
    }

    // Vanilla checkPressed: the plate takes the power the entities on it give, clicks when it goes on or off, and checks
    // again later while pressed.
    private async ValueTask CheckPressedAsync(Vector position, IBlock state, Entity? source)
    {
        var old = Signal(state);
        var signal = this.SignalStrength(position, state);
        if (signal != old)
            await this.WriteAsync(position, WithSignal(state, signal));

        if (signal == 0 && old > 0)
        {
            level.PlaySound(position, PlateSound(state, on: false));
            level.EmitGameEvent(MobGameEvent.BlockDeactivate, position, source);
        }
        else if (signal > 0 && old == 0)
        {
            level.PlaySound(position, PlateSound(state, on: true));
            level.EmitGameEvent(MobGameEvent.BlockActivate, position, source);
        }

        if (signal > 0)
            level.ScheduleBlockTick(position, state, Delay(state));
    }

    // Vanilla getSignalStrength: 15 for anything on a plain plate, and the weight for a weighted one.
    private int SignalStrength(Vector position, IBlock state)
    {
        var min = new VectorD(position.X + TouchInset, position.Y, position.Z + TouchInset);
        var max = new VectorD(position.X + 1 - TouchInset, position.Y + TouchHeight, position.Z + 1 - TouchInset);
        var livingOnly = CountsLivingOnly(state);
        var count = 0;
        foreach (var entity in level.EntitiesIn(min, max))
        {
            if (EntityContactPass.IgnoresBlockTriggers(entity) || livingOnly && entity is not Living)
                continue;

            count++;
        }

        if (IsWeighted(state))
            return WeightedStrength(count, MaxWeight(state));

        return count > 0 ? RedstoneEngine.MaxSignal : 0;
    }

    private static IBlock WithSignal(IBlock state, int signal) => IsWeighted(state)
        ? state.WithProperty("power", signal.ToString(System.Globalization.CultureInfo.InvariantCulture))
        : state.WithProperty("powered", signal > 0);

    /// <summary>
    /// Commits a plate's state, then notifies the block it stands on. Vanilla plates do this with <c>updateNeighborsAt(pos.below())</c>,
    /// so a door or wire against the support sees the press.
    /// </summary>
    private async ValueTask WriteAsync(Vector position, IBlock block)
    {
        await level.SetBlockAsync(position, block);
        await level.NotifyNeighborsAsync(position.Offset(BlockFace.Down));
    }

    /// <summary>
    /// The plate's signal: pressed plates output full power (or their scaled weight), whichever way it's read.
    /// </summary>
    internal static int Signal(IBlock state)
    {
        if (IsWeighted(state))
            return int.TryParse(state.GetProperty("power"), out var power) ? power : 0;

        return state.GetProperty("powered") == "true" ? RedstoneEngine.MaxSignal : 0;
    }
}
