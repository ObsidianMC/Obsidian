using Obsidian.WorldData.BlockNotifications;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The vanilla daylight cycle as the overworld timeline defines it (<c>data/minecraft/timeline/day.json</c> and
/// <c>data/minecraft/dimension_type/overworld.json</c>). Overworld only; other dimensions' timelines are not modelled.
/// </summary>
internal static class DaylightCycle
{
    private const int Period = 24000;

    // gameplay/sky_light_level: linear keyframes, multiplied onto the base sky light level of 15. Values are float32 as in vanilla.
    private static readonly (int Tick, float Value)[] SkyLightKeys =
    [
        (133, 1.0f), (11867, 1.0f), (13670, 0.26666668f), (22330, 0.26666668f),
    ];

    // visual/sun_angle: 360 at tick 6000 sweeping to 0 over one period, eased by this cubic bezier.
    private const double SunEaseX1 = 0.362, SunEaseY1 = 0.241, SunEaseX2 = 0.638, SunEaseY2 = 0.759;
    private const int SunAngleStartTick = 6000;

    /// <summary>
    /// Sky darkness, 0 to 11, that the level subtracts from sky light (vanilla <c>Level.updateSkyBrightness</c>: <c>(int)(15 - level)</c>).
    /// </summary>
    public static int SkyDarken(long dayTime)
    {
        var level = 15f * Keyframe(SkyLightKeys, dayTime);
        return (int)(15f - level);
    }

    /// <summary>The sun angle in degrees.</summary>
    public static float SunAngleDegrees(long dayTime)
    {
        var phase = ((dayTime - SunAngleStartTick) % Period + Period) % Period / (double)Period;
        var eased = CubicBezierEase(phase);
        return (float)(360.0 * (1.0 - eased));
    }

    /// <summary>
    /// Linear interpolation over a keyframe track that wraps at the period. Keyframes are sorted by tick.
    /// </summary>
    private static float Keyframe((int Tick, float Value)[] keys, long dayTime)
    {
        var time = (int)(((dayTime % Period) + Period) % Period);
        if (time < keys[0].Tick)
            time += Period;

        for (var i = 0; i < keys.Length; i++)
        {
            var (fromTick, fromValue) = keys[i];
            var (toTick, toValue) = i + 1 < keys.Length ? keys[i + 1] : (keys[0].Tick + Period, keys[0].Value);
            if (time < fromTick || time >= toTick)
                continue;

            var fraction = (float)(time - fromTick) / (toTick - fromTick);
            return fromValue + (toValue - fromValue) * fraction;
        }

        return keys[^1].Value;
    }

    /// <summary>Solves the cubic bezier for x = <paramref name="x"/> and returns its y.</summary>
    private static double CubicBezierEase(double x)
    {
        double BezierX(double t) => 3 * (1 - t) * (1 - t) * t * SunEaseX1 + 3 * (1 - t) * t * t * SunEaseX2 + t * t * t;
        double BezierY(double t) => 3 * (1 - t) * (1 - t) * t * SunEaseY1 + 3 * (1 - t) * t * t * SunEaseY2 + t * t * t;

        var low = 0.0;
        var high = 1.0;
        for (var i = 0; i < 60; i++)
        {
            var mid = (low + high) / 2;
            if (BezierX(mid) < x)
                low = mid;
            else
                high = mid;
        }

        return BezierY((low + high) / 2);
    }
}

/// <summary>
/// The daylight detector's output and its periodic update (vanilla <c>DaylightDetectorBlock</c>, checked against the 1.21.11 bytecode).
/// </summary>
internal static class DaylightDetector
{
    public const int BlockId = BlockIds.DaylightDetector;
    public const string BlockClassName = "DaylightDetectorBlock";

    // Vanilla updates from its block entity ticker, on game ticks where the game time is a multiple of 20.
    private const int Interval = 20;

    /// <summary>The power for a sky light value, time, and inversion (vanilla <c>DaylightDetectorBlock.updateSignalStrength</c>).</summary>
    public static int Power(int skyLight, long dayTime, bool inverted)
    {
        var raw = skyLight - DaylightCycle.SkyDarken(dayTime);
        if (inverted)
            return Clamp(15 - raw);

        var power = raw;
        if (raw > 0)
        {
            var angle = DaylightCycle.SunAngleDegrees(dayTime) * 0.017453292f;
            var target = angle < MathF.PI ? 0f : 2 * MathF.PI;
            angle += (target - angle) * 0.2f;
            power = (int)MathF.Floor(raw * (float)Math.Cos(angle) + 0.5f);
        }

        return Clamp(power);
    }

    /// <summary>Ticks until the next multiple of 20 game ticks, which is 20 when the game time is already one.</summary>
    public static int NextDelay(long gameTime) => Interval - (int)(gameTime % Interval);

    /// <summary>
    /// Recomputes a loaded detector's power, writes it only when it changed, and schedules the next update.
    /// </summary>
    public static async ValueTask RefreshAsync(IRedstoneLevel world, Vector position)
    {
        var block = world.GetLoadedBlock(position);
        if (block is null || block.BlockClass() != BlockClassName)
            return;

        var power = Power(world.GetSkyLight(position), world.DayTime, block.GetProperty("inverted") == "true");
        if (power.ToString() != block.GetProperty("power"))
            await world.WriteAsync(position, block.WithProperty("power", power.ToString()), notifyNeighbors: true);

        world.ScheduleBlockTick(position, block, NextDelay(world.GameTime));
    }

    /// <summary>
    /// A right-click inverts the detector and recomputes its power at once. Neighbors are notified only if the power changed,
    /// as vanilla's <c>useWithoutItem</c> writes the inversion with flag 2 and the power update with flag 3.
    /// </summary>
    public static async ValueTask ToggleAsync(IRedstoneLevel world, Vector position, IBlock block)
    {
        var inverted = block.GetProperty("inverted") != "true";
        var power = Power(world.GetSkyLight(position), world.DayTime, inverted).ToString();
        var next = block.WithProperty("inverted", inverted ? "true" : "false").WithProperty("power", power);
        await world.WriteAsync(position, next, notifyNeighbors: power != block.GetProperty("power"));
    }

    private static int Clamp(int value) => Math.Clamp(value, 0, 15);
}

/// <summary>Emits the stored power in every direction (vanilla <c>getSignal</c>); it gives no direct power.</summary>
internal sealed class DaylightDetectorSignal : IRedstoneBehavior
{
    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        int.Parse(state.GetProperty("power") ?? "0");

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) => 0;
}

/// <summary>Starts a new detector's periodic update on the next 20-tick boundary.</summary>
internal sealed class DaylightDetectorPlacement(IRedstoneLevel level) : IPlacementBehavior
{
    public ValueTask OnPlacedAsync(BlockChange change, IBlockChangeWorld _)
    {
        level.ScheduleBlockTick(change.Position, change.New, DaylightDetector.NextDelay(level.GameTime));
        return ValueTask.CompletedTask;
    }
}
