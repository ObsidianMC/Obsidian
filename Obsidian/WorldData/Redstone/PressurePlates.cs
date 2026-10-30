using Obsidian.WorldData;
using Obsidian.Entities;
using Obsidian.WorldData.BlockNotifications;
using Obsidian.WorldData.BlockTicks;
using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Pressure plates (vanilla <c>BasePressurePlateBlock</c> and <c>WeightedPressurePlateBlock</c>). An entity standing in the plate's top
/// 1/16 block layer presses it. Pressing schedules a persisted release tick; when that tick fires, the plate stays pressed if an entity is
/// still on it, and otherwise releases.
/// </summary>
/// <remarks>
/// Detection runs once per level tick, after block ticks and before entities tick, against the entity positions at that time.
/// Sensitivity follows vanilla <c>PressurePlateSensitivity</c>: stone and polished blackstone plates are pressed by living entities
/// (players and mobs) only; wooden plates and weighted plates by any non-spectator entity, including item entities and projectiles.
/// </remarks>
/// <summary>
/// What pressure plates need from a world: committed block reads and writes, and persisted delayed ticks.
/// </summary>
internal interface IPressurePlateWorld : IBlockChangeWorld
{
    void SchedulePlateTick(Vector position, IBlock block, int delay);
}

internal sealed class PressurePlates
{
    // Vanilla BasePressurePlateBlock.getPressedTime and WeightedPressurePlateBlock.getPressedTime.
    private const int PlateDelay = 20;
    private const int WeightedDelay = 10;

    // Vanilla plate top layer: an entity whose bottom is below this height overlaps the plate.
    private const double PlateHeight = 1d / 16d;

    // The generated block tag of every pressure plate (vanilla #minecraft:pressure_plates), the set this engine drives.
    private static readonly ImmutableArray<int> plates = TagsRegistry.Block.PressurePlates.Entries;

    private readonly IPressurePlateWorld world;

    // Plates that had an entity on them at the last detection pass, by sensitivity. Release ticks read them to decide whether to stay pressed.
    private HashSet<Vector> occupied = [];
    private HashSet<Vector> occupiedByLiving = [];

    public PressurePlates(IPressurePlateWorld world)
    {
        this.world = world;
    }

    public static bool IsPlate(IBlock state) => plates.Contains(state.RegistryId);

    private static bool IsWeighted(IBlock state) => state.BlockClass() == "WeightedPressurePlateBlock";

    // Vanilla's plate clicks: weighted plates use the metal sounds, the others their block set's.
    private static string PlateSound(IBlock state, bool on) =>
        $"minecraft:block.{(IsWeighted(state) ? "metal" : VanillaPlacement.SoundMaterial(state))}_pressure_plate.{(on ? "click_on" : "click_off")}";

    private enum Sensing
    {
        Any,
        Living
    }

    // Vanilla PressurePlateSensitivity: stone and polished blackstone plates use MOBS; the other plates use EVERYTHING.
    private static Sensing Sensitivity(IBlock state) =>
        TagsRegistry.Block.StonePressurePlates.Entries.Contains(state.RegistryId) ? Sensing.Living : Sensing.Any;

    private static int Delay(IBlock state) => IsWeighted(state) ? WeightedDelay : PlateDelay;

    /// <summary>
    /// Vanilla <c>getSignalStrength</c>: a weighted plate outputs its entity count scaled to 15 over its maximum weight.
    /// </summary>
    internal static int WeightedStrength(int count, int maxWeight)
    {
        var clamped = Math.Min(count, maxWeight);
        return (int)Math.Ceiling(clamped * (double)RedstoneEngine.MaxSignal / maxWeight);
    }

    private static int MaxWeight(IBlock state) => state.Is(BlockIds.HeavyWeightedPressurePlate) ? 150 : 15;

    /// <summary>
    /// Registers the persisted release for every plate. The callback runs on the level's entity-action path, after block ticks.
    /// </summary>
    public void RegisterTicks(LevelBlockTicks ticks, IRedstoneLevel level)
    {
        foreach (var id in plates)
            ticks.Register(id, (position, _) => level.EnqueueSimulation(() => this.ReleaseAsync(position)));
    }

    /// <summary>
    /// Runs one detection pass over the entities that tick this level tick.
    /// </summary>
    public ValueTask UpdateAsync(IEnumerable<IEntity> entities) => this.ApplyAsync(Occupancy(entities));

    /// <summary>
    /// How many entities overlap each plate position: <see cref="Any"/> counts every non-spectator entity (wooden and weighted plates);
    /// <see cref="Living"/> counts only players and mobs (stone plates).
    /// </summary>
    internal sealed record PlateOccupancy(IReadOnlyDictionary<Vector, int> Any, IReadOnlyDictionary<Vector, int> Living);

    /// <summary>
    /// Counts the entities overlapping each plate position, split by sensitivity.
    /// </summary>
    internal static PlateOccupancy Occupancy(IEnumerable<IEntity> entities)
    {
        var any = new Dictionary<Vector, int>();
        var living = new Dictionary<Vector, int>();
        foreach (var entity in entities)
        {
            if (entity is IPlayer { GameMode: GameMode.Spectator })
                continue;

            var isLiving = entity is IPlayer or Mob;

            var box = entity.Dimension.CreateBBFromPosition(entity.Position);
            var minX = (int)Math.Floor(box.Min.X);
            var maxX = (int)Math.Floor(box.Max.X - 1e-7);
            var minZ = (int)Math.Floor(box.Min.Z);
            var maxZ = (int)Math.Floor(box.Max.Z - 1e-7);
            var y = (int)Math.Floor(box.Min.Y);
            if (box.Min.Y >= y + PlateHeight)
                continue;

            for (var x = minX; x <= maxX; x++)
            {
                for (var z = minZ; z <= maxZ; z++)
                {
                    var position = new Vector(x, y, z);
                    any[position] = any.GetValueOrDefault(position) + 1;
                    if (isLiving)
                        living[position] = living.GetValueOrDefault(position) + 1;
                }
            }
        }

        return new PlateOccupancy(any, living);
    }

    /// <summary>
    /// Presses the plates under the given occupancy and schedules their release. Plates not in <paramref name="counts"/> are left to their
    /// release tick.
    /// </summary>
    internal ValueTask ApplyAsync(IReadOnlyDictionary<Vector, int> counts) => this.ApplyAsync(new PlateOccupancy(counts, counts));

    internal async ValueTask ApplyAsync(PlateOccupancy occupancy)
    {
        this.occupied = occupancy.Any.Keys.ToHashSet();
        this.occupiedByLiving = occupancy.Living.Keys.ToHashSet();
        var pressing = occupancy.Any.Keys.Union(occupancy.Living.Keys).OrderBy(position => position.X).ThenBy(position => position.Y).ThenBy(position => position.Z);
        foreach (var position in pressing)
        {
            var state = this.world.GetLoadedBlock(position);
            if (state is null || !IsPlate(state))
                continue;

            var count = Sensitivity(state) == Sensing.Living ? occupancy.Living.GetValueOrDefault(position) : occupancy.Any.GetValueOrDefault(position);
            if (count == 0)
                continue;

            if (IsWeighted(state))
            {
                var strength = WeightedStrength(count, MaxWeight(state));
                if (state.GetProperty("power") == strength.ToString())
                    continue;

                var wasReleased = state.GetProperty("power") == "0";
                await this.WriteAsync(position, state.WithProperty("power", strength.ToString()));
                if (wasReleased)
                {
                    this.world.SchedulePlateTick(position, state, WeightedDelay);
                    this.world.PlaySound(position, PlateSound(state, on: true));
                }
            }
            else if (state.GetProperty("powered") != "true")
            {
                await this.WriteAsync(position, state.WithProperty("powered", true));
                this.world.SchedulePlateTick(position, state, PlateDelay);
                this.world.PlaySound(position, PlateSound(state, on: true));
            }
        }
    }

    /// <summary>
    /// Release tick: a plate that still has an entity on it re-arms; otherwise it releases.
    /// </summary>
    internal async ValueTask ReleaseAsync(Vector position)
    {
        var state = this.world.GetLoadedBlock(position);
        if (state is null || !IsPlate(state))
            return;

        var stillOccupied = Sensitivity(state) == Sensing.Living ? this.occupiedByLiving.Contains(position) : this.occupied.Contains(position);
        if (stillOccupied)
        {
            this.world.SchedulePlateTick(position, state, Delay(state));
            return;
        }

        if (IsWeighted(state))
        {
            if (state.GetProperty("power") != "0")
            {
                await this.WriteAsync(position, state.WithProperty("power", "0"));
                this.world.PlaySound(position, PlateSound(state, on: false));
            }
        }
        else if (state.GetProperty("powered") == "true")
        {
            await this.WriteAsync(position, state.WithProperty("powered", false));
            this.world.PlaySound(position, PlateSound(state, on: false));
        }
    }

    /// <summary>
    /// Commits a plate's state, then notifies the block it stands on. Vanilla plates do this with <c>updateNeighborsAt(pos.below())</c>,
    /// so a door or wire against the support sees the press.
    /// </summary>
    private async ValueTask WriteAsync(Vector position, IBlock block)
    {
        await this.world.SetBlockAsync(position, block);
        await this.world.NotifyNeighborsAsync(position.Offset(BlockFace.Down));
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
