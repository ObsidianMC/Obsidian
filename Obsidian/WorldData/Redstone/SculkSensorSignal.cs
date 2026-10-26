using Obsidian.WorldData.Features;

namespace Obsidian.WorldData.Redstone;

/// <summary>
/// Sculk sensors (vanilla <c>SculkSensorBlock</c> and <c>CalibratedSculkSensorBlock</c>). Their output is their <c>power</c>
/// state, which the sensor writes through the block change pipeline, so the engine reads it like any other source.
/// </summary>
/// <remarks>
/// Vanilla: a sensor gives its power in every direction, and its direct signal only upward, to the block it sits on. A calibrated
/// sensor gives nothing toward its facing.
/// </remarks>
internal sealed class SculkSensorSignal(bool calibrated) : IRedstoneBehavior
{
    public const string SensorClass = "SculkSensorBlock";
    public const string CalibratedClass = "CalibratedSculkSensorBlock";

    public static void Register(RedstoneBehaviors redstone)
    {
        redstone.Register(SensorClass, new SculkSensorSignal(calibrated: false));
        redstone.Register(CalibratedClass, new SculkSensorSignal(calibrated: true));
    }

    public int GetSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal)
    {
        if (calibrated && direction == FeatureHelpers.ParseFace(state.GetProperty("facing")))
            return 0;

        return Power(state);
    }

    public int GetDirectSignal(IBlock state, Vector position, BlockFace direction, bool wiresGiveSignal) =>
        direction == BlockFace.Down ? this.GetSignal(state, position, direction, wiresGiveSignal) : 0;

    private static int Power(IBlock state) => int.Parse(state.GetProperty("power"));
}
