namespace Obsidian.WorldData.Portals;

/// <summary>Destination chunks held while a portal search waits for background generation.</summary>
internal sealed class PortalArea(AbstractLevel level) : IDisposable
{
    private readonly List<(int X, int Z)> tickets = [];
    public Dictionary<(int X, int Z), IChunk> Chunks { get; } = [];

    public IBlock Read(Vector position) => !level.IsOutsideBuildHeight(position.Y) &&
        this.Chunks.TryGetValue((position.X >> 4, position.Z >> 4), out var chunk)
            ? chunk.GetBlock(position.X, position.Y, position.Z) : BlocksRegistry.VoidAir;
    public ValueTask<IBlock?> ReadAsync(Vector position) => new(this.Read(position));

    public static async Task<PortalArea?> LoadAsync(AbstractLevel level, Vector center, int radius, Func<bool> canContinue,
        bool generateMissing = true)
    {
        var area = new PortalArea(level);
        try
        {
            for (var x = (center.X - radius) >> 4; x <= (center.X + radius) >> 4; x++)
                for (var z = (center.Z - radius) >> 4; z <= (center.Z + radius) >> 4; z++)
                {
                    level.PinPortalChunk(x, z, 1);
                    area.tickets.Add((x, z));
                }
            var pending = new HashSet<(int X, int Z)>(area.tickets);
            while (pending.Count > 0)
            {
                if (!canContinue())
                {
                    area.Dispose();
                    return null;
                }
                foreach (var (x, z) in pending.ToArray())
                {
                    if (await level.GetChunkAsync(x, z, generateMissing) is { IsGenerated: true } chunk)
                    {
                        area.Chunks[(x, z)] = chunk;
                        pending.Remove((x, z));
                    }
                    else if (!generateMissing)
                        pending.Remove((x, z));
                }
                if (pending.Count > 0)
                    await Task.Delay(25);
            }
            return area;
        }
        catch
        {
            area.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        foreach (var (x, z) in this.tickets)
            level.PinPortalChunk(x, z, -1);
        this.tickets.Clear();
    }
}
