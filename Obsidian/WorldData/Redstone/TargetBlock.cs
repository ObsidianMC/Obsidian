using Obsidian.WorldData.BlockTicks;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// A target block (vanilla <c>TargetBlock</c>, checked against the 1.21.11 bytecode). A projectile that enters it sets
/// <c>power</c> from where it hit the face, and the pulse ends after 20 ticks for an arrow (8 for other projectiles). A second hit
/// while a pulse is pending does nothing. The power is a weak signal in every direction.
/// </summary>
internal sealed class TargetBlock(IRedstoneLevel level) : IRedstoneBehavior
{
    public const int BlockId = BlockIds.Target;

    /// <summary>The pulse length for an arrow (vanilla <c>AbstractArrow</c>).</summary>
    public const int ArrowPulseTicks = 20;

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => Power(state);

    public static int Power(IBlock state) => int.Parse(state.GetProperty("power"));

    /// <summary>
    /// The power for a hit at <paramref name="location"/> on <paramref name="face"/>: the nearer the hit is to the centre of the face,
    /// the stronger, from 1 at the edge to 15 at the centre. Vanilla takes the larger offset of the two in-face axes from the centre,
    /// then rounds up (<c>Mth.ceil</c>, checked in the bytecode), so 7.5 becomes 8.
    /// </summary>
    public static int StrengthAt(VectorD location, BlockFace face)
    {
        var dx = Math.Abs(Fraction(location.X) - 0.5);
        var dy = Math.Abs(Fraction(location.Y) - 0.5);
        var dz = Math.Abs(Fraction(location.Z) - 0.5);
        var offset = face switch
        {
            BlockFace.East or BlockFace.West => Math.Max(dy, dz),
            BlockFace.Up or BlockFace.Down => Math.Max(dx, dz),
            _ => Math.Max(dx, dy),
        };
        var fraction = Math.Clamp((0.5 - offset) / 0.5, 0, 1);
        return Math.Max(1, (int)Math.Ceiling(15 * fraction));
    }

    /// <summary>
    /// A projectile hit: sets the power and schedules the pulse's end, unless a pulse is already pending (vanilla
    /// <c>onProjectileHit</c>). Other blocks are ignored.
    /// </summary>
    public async ValueTask HitAsync(Vector position, VectorD location, BlockFace face, int delay)
    {
        var block = level.GetLoadedBlock(position);
        if (block is null || !block.Is(BlockId) || level.IsBlockTickScheduled(position, BlocksRegistry.GetDefault(BlockId).UnlocalizedName))
            return;

        var powered = block.WithProperty("power", StrengthAt(location, face).ToString());
        await level.WriteAsync(position, powered, notifyNeighbors: true);
        level.ScheduleBlockTick(position, powered, delay);
    }

    /// <summary>The scheduled end of a pulse: power goes to 0 if the block still has power.</summary>
    public async ValueTask TickAsync(Vector position)
    {
        var block = level.GetLoadedBlock(position);
        if (block is null || !block.Is(BlockId) || Power(block) == 0)
            return;

        await level.WriteAsync(position, block.WithProperty("power", "0"), notifyNeighbors: true);
    }

    public static TargetBlock Register(IRedstoneLevel level, LevelBlockTicks ticks, RedstoneBehaviors redstone)
    {
        var target = new TargetBlock(level);
        redstone.Register(BlocksRegistry.GetDefault(BlockId).BlockClass(), target);
        ticks.Register(BlockId, (position, _) => level.EnqueueSimulation(() => target.TickAsync(position)));
        return target;
    }

    private static double Fraction(double value) => value - Math.Floor(value);
}
