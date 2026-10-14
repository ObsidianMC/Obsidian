using Obsidian.API.BlockEntities;
using Obsidian.API.Blocks;
using Obsidian.API.World.Features.Trees;
using Obsidian.Entities;
using Obsidian.Nbt;
using Obsidian.WorldData.Features;
using System.Threading;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The comparator, like vanilla's <c>ComparatorBlock</c>: its input is the power behind it, or the analog output of
/// the block behind it (directly, or behind a conductor), which it compares with or subtracts its sides' power from.
/// Its output is kept in its block entity.
/// </summary>
internal sealed class Comparator : Diode
{
    // Vanilla's comparator delay.
    private const int ComparatorDelay = 2;

    protected override bool SideInputDiodesOnly => false;

    // Vanilla's useWithoutItem: the mode switches, with a click.
    public override bool Use(LevelBlockUpdates level, Vector position, IBlock state, IPlayer player, bool mayBuild)
    {
        if (!mayBuild)
            return false;

        var subtract = state.GetProperty("mode") != "subtract";
        var toggled = state.WithProperty("mode", subtract ? "subtract" : "compare");
        level.PlaySound(position, "minecraft:block.comparator.click", 0.3f, subtract ? 0.55f : 0.5f, player);
        level.Set(position, toggled, BlockUpdateFlags.Clients);
        this.RefreshOutputState(level, position, toggled);
        return true;
    }

    public override void Tick(LevelBlockUpdates level, Vector position, IBlock state) =>
        this.RefreshOutputState(level, position, state);

    protected override int Delay(IBlock state) => ComparatorDelay;

    protected override int OutputSignal(LevelBlockUpdates level, Vector position, IBlock state) =>
        level.GetBlockEntity(position) is ComparatorBlockEntity comparator ? comparator.OutputSignal : 0;

    protected override bool ShouldTurnOn(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var input = this.InputSignal(level, position, state);
        if (input == 0)
            return false;

        var sides = this.AlternateSignal(level, position, state);
        return input > sides || input == sides && !IsSubtracting(state);
    }

    // Vanilla ComparatorBlock.getInputSignal: a block behind with an analog output gives that instead of power, and
    // so does one behind a conductor there, or an item frame on that conductor, unless the conductor is fully powered.
    protected override int InputSignal(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var signal = base.InputSignal(level, position, state);
        var facing = Facing(state);
        var behind = position.Offset(facing);
        var input = level.GetBlock(behind);

        if (level.HasAnalogOutput(input))
            return level.AnalogOutputOf(input, behind, facing.Opposite());

        if (signal >= 15 || !input.IsRedstoneConductor())
            return signal;

        var further = behind.Offset(facing);
        var block = level.GetBlock(further);
        var analog = int.MinValue;
        if (ItemFrameAt(level, facing, further) is ItemFrame frame)
            analog = frame.Item is { IsAir: false } ? frame.Rotation % 8 + 1 : 0;

        if (level.HasAnalogOutput(block))
            analog = Math.Max(analog, level.AnalogOutputOf(block, further, facing.Opposite()));

        return analog != int.MinValue ? analog : signal;
    }

    // Vanilla ComparatorBlock.checkTickOnNeighbor: an output or state that would change schedules its tick.
    protected override void CheckTickOnNeighbor(LevelBlockUpdates level, Vector position, IBlock state)
    {
        if (level.WillTickThisTick(position, state))
            return;

        var output = this.CalculateOutputSignal(level, position, state);
        var current = this.OutputSignal(level, position, state);
        if (output == current && IsPowered(state) == this.ShouldTurnOn(level, position, state))
            return;

        var priority = ShouldPrioritize(level, position, state) ? TickPriority.High : TickPriority.Normal;
        level.ScheduleTick(position, state, ComparatorDelay, priority);
    }

    private static bool IsSubtracting(IBlock state) => state.GetProperty("mode") == "subtract";

    // Vanilla getItemFrame: the one item frame in the block, hanging on the face toward the comparator's input.
    private static ItemFrame? ItemFrameAt(LevelBlockUpdates level, BlockFace facing, Vector position)
    {
        var frames = level.EntitiesIn((VectorD)position, (VectorD)(position + new Vector(1, 1, 1)))
            .OfType<ItemFrame>()
            .Where(frame => frame.Facing == facing)
            .Take(2)
            .ToList();

        return frames.Count == 1 ? frames[0] : null;
    }

    // Vanilla calculateOutputSignal: nothing when the sides beat the input, else the input, less the sides'
    // power when subtracting.
    private int CalculateOutputSignal(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var input = this.InputSignal(level, position, state);
        if (input == 0)
            return 0;

        var sides = this.AlternateSignal(level, position, state);
        if (sides > input)
            return 0;

        return IsSubtracting(state) ? input - sides : input;
    }

    // Vanilla refreshOutputState: takes the new output, then turns on or off, and the block in front hears of it.
    private void RefreshOutputState(LevelBlockUpdates level, Vector position, IBlock state)
    {
        var output = this.CalculateOutputSignal(level, position, state);
        var previous = 0;
        if (level.GetBlockEntity(position) is ComparatorBlockEntity comparator)
        {
            previous = comparator.OutputSignal;
            comparator.OutputSignal = output;
        }

        if (previous == output && IsSubtracting(state))
            return;

        var shouldTurnOn = this.ShouldTurnOn(level, position, state);
        var powered = IsPowered(state);
        if (powered && !shouldTurnOn)
            level.Set(position, state.WithProperty("powered", false), BlockUpdateFlags.Clients);
        else if (!powered && shouldTurnOn)
            level.Set(position, state.WithProperty("powered", true), BlockUpdateFlags.Clients);

        UpdateNeighborsInFront(level, position, state);
    }
}

/// <summary>
/// A comparator's block entity, like vanilla's <c>ComparatorBlockEntity</c>: the power it outputs, saved as
/// <c>OutputSignal</c>.
/// </summary>
internal sealed class ComparatorBlockEntity : BlockEntity
{
    private int outputSignal;

    /// <summary>
    /// The power the comparator gives while powered, set under the level's tick lock.
    /// </summary>
    public int OutputSignal
    {
        get => Volatile.Read(ref this.outputSignal);
        set => Volatile.Write(ref this.outputSignal, value);
    }

    // It's saved as it is whenever its chunk is.
    internal override bool SavesOnAnyThread => true;

    protected internal override void Load(NbtCompound tag) =>
        this.OutputSignal = tag.TryGetTagValue<int>("OutputSignal", out var signal) ? signal : 0;

    protected internal override void Save(NbtCompound tag) =>
        tag.Add(new NbtTag<int>("OutputSignal", this.OutputSignal));
}
