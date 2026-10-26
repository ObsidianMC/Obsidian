namespace Obsidian.WorldData.Redstone;

/// <summary>
/// The redstone torches of a level that turned off lately, for their burning out: like vanilla's, a torch that turned
/// off 8 times within 60 game ticks is burnt out until fewer did in the last 60.
/// </summary>
internal sealed class TorchBurnout
{
    // Vanilla's burn-out window and the turn-offs within it that burn a torch out.
    private const int Window = 60;
    private const int TurnOffsToBurnOut = 8;

    // In the order they happened, so that the expired ones are at the start.
    private readonly List<(Vector Position, long Time)> turnOffs = [];

    /// <summary>
    /// Counts the torch at <paramref name="position"/> turning off <paramref name="now"/>.
    /// </summary>
    /// <returns>Whether that burns it out.</returns>
    public bool TurnedOff(Vector position, long now)
    {
        this.Expire(now);
        this.turnOffs.Add((position, now));

        return this.CountAt(position) >= TurnOffsToBurnOut;
    }

    /// <summary>
    /// Whether the torch at <paramref name="position"/> is burnt out <paramref name="now"/>.
    /// </summary>
    public bool IsBurntOut(Vector position, long now)
    {
        this.Expire(now);
        return this.CountAt(position) >= TurnOffsToBurnOut;
    }

    private void Expire(long now)
    {
        var expired = 0;
        while (expired < this.turnOffs.Count && this.turnOffs[expired].Time < now - Window)
            expired++;

        this.turnOffs.RemoveRange(0, expired);
    }

    private int CountAt(Vector position) => this.turnOffs.Count(turnOff => turnOff.Position == position);
}
