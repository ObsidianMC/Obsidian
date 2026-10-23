namespace Obsidian.WorldData;

/// <summary>
/// The chunks within simulation distance of a level's non-spectator players: those whose mobs, block entities and
/// random ticks run, like vanilla's chunk map works out once a tick. It's updated from the players' chunks at the start
/// of each tick, and only the squares of players who changed chunk, joined or left are counted again, since the level's
/// tick asks whether a chunk is simulated for every mob and loaded chunk.
/// </summary>
/// <remarks>
/// The level updates it at the start of its tick, under its simulation gate. Reads are safe from any thread, as the
/// counts are a concurrent dictionary: one made while the tick updates them sees some players' new squares.
/// </remarks>
internal sealed class SimulationArea
{
    // How many players simulate each chunk, and the chunk each player was counted around.
    private readonly ConcurrentDictionary<long, int> chunks = [];
    private readonly Dictionary<IPlayer, (int X, int Z)> centers = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<IPlayer> current = new(ReferenceEqualityComparer.Instance);
    private int distance = -1;

    /// <summary>The simulated chunks, packed with <see cref="NumericsHelper.IntsToLong"/>, in no particular order.</summary>
    public IEnumerable<long> Chunks
    {
        get
        {
            // Read in place: the dictionary's Keys copies them under all its locks.
            foreach (var (key, _) in this.chunks)
                yield return key;
        }
    }

    public bool Contains(int chunkX, int chunkZ) => this.chunks.ContainsKey(NumericsHelper.IntsToLong(chunkX, chunkZ));

    /// <summary>Counts the squares around <paramref name="players"/>, the level's non-spectator players now.</summary>
    public void Update(IEnumerable<IPlayer> players, int simulationDistance)
    {
        if (simulationDistance != this.distance)
        {
            this.chunks.Clear();
            this.centers.Clear();
            this.distance = simulationDistance;
        }

        this.current.Clear();
        foreach (var player in players)
        {
            this.current.Add(player);
            var center = Region.ChunkOf(player.Position);
            if (this.centers.TryGetValue(player, out var previous))
            {
                if (previous == center)
                    continue;

                this.Count(previous, -1);
            }

            this.centers[player] = center;
            this.Count(center, 1);
        }

        if (this.centers.Count == this.current.Count)
            return;

        List<IPlayer>? gone = null;
        foreach (var (player, _) in this.centers)
        {
            if (!this.current.Contains(player))
                (gone ??= []).Add(player);
        }

        foreach (var player in gone!)
        {
            this.Count(this.centers[player], -1);
            this.centers.Remove(player);
        }
    }

    private void Count((int X, int Z) center, int change)
    {
        for (var x = center.X - this.distance; x <= center.X + this.distance; x++)
        {
            for (var z = center.Z - this.distance; z <= center.Z + this.distance; z++)
            {
                var key = NumericsHelper.IntsToLong(x, z);
                var count = this.chunks.GetValueOrDefault(key) + change;
                if (count > 0)
                    this.chunks[key] = count;
                else
                    this.chunks.TryRemove(key, out _);
            }
        }
    }
}
