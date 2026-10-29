using Obsidian.Registries;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// The game time and the world clocks' states (26.1). Clients take the time of day, the moon and the sky from the
/// clocks their dimension types name.
/// </summary>
public partial class SetTimePacket(long worldAge, IReadOnlyList<SetTimePacket.Clock> clocks)
{
    private const string ClockRegistry = "minecraft:world_clock";

    public long WorldAge { get; } = worldAge;

    public IReadOnlyList<Clock> Clocks { get; } = clocks;

    /// <summary>
    /// The full time sync vanilla sends on joining and changing level: the game time and every clock.
    /// </summary>
    /// <remarks>
    /// Obsidian keeps one clock per level, its time, which drives both of vanilla's clocks (the overworld's day and the
    /// End's). Like vanilla, a clock that doesn't advance (<c>advance_time</c> is off) is sent with a rate of 0.
    /// </remarks>
    public static SetTimePacket Of(LevelData levelData)
    {
        var rate = levelData.GetBooleanRule("advance_time") ? 1f : 0f;
        var registry = VanillaSyncedRegistries.All.First(registry => registry.Name == ClockRegistry);
        return new(levelData.Time, [.. registry.Entries.Select((_, id) => new Clock(id, levelData.Time, 0f, rate))]);
    }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteLong(this.WorldAge);

        writer.WriteVarInt(this.Clocks.Count);
        foreach (var clock in this.Clocks)
        {
            writer.WriteVarInt(clock.Id);
            writer.WriteVarLong(clock.TotalTicks);
            writer.WriteSingle(clock.PartialTick);
            writer.WriteSingle(clock.Rate);
        }
    }

    /// <summary>
    /// A world clock's state, as vanilla's <c>ClockNetworkState</c> sends it.
    /// </summary>
    /// <param name="Id">The clock's network id in <c>minecraft:world_clock</c>.</param>
    /// <param name="TotalTicks">The ticks the clock has counted.</param>
    /// <param name="PartialTick">How far the clock is into its next tick.</param>
    /// <param name="Rate">The ticks the clock counts per game tick, which clients advance it by between syncs.</param>
    public readonly record struct Clock(int Id, long TotalTicks, float PartialTick, float Rate);
}
