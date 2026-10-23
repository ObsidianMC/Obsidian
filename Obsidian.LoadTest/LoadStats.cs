using System.Diagnostics;

namespace Obsidian.LoadTest;

/// <summary>
/// What the bots observed, shared by all of them and reported every interval (see <see cref="Report"/>).
/// </summary>
internal sealed class LoadStats
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, int> disconnectReasons = [];

    // Each dimension's latest game time a bot was sent, and when; the server sends it every 20 of the level's ticks.
    private readonly Dictionary<string, (long GameTime, long Timestamp)> latestTimes = [];
    private readonly Dictionary<string, (long GameTime, long Timestamp)> windowStartTimes = [];

    private long receivedBytes;
    private long chunks;
    private int connecting;
    private int playing;
    private int joinsTotal;
    private long joinMillisecondsTotal;
    private long spawnAreaMillisecondsTotal;
    private int spawnAreasLoaded;
    private long lastReport = Stopwatch.GetTimestamp();

    public int Playing => Volatile.Read(ref this.playing);

    public void AddReceivedBytes(int count) => Interlocked.Add(ref this.receivedBytes, count);

    public void AddChunks(int count) => Interlocked.Add(ref this.chunks, count);

    public void Connecting() => Interlocked.Increment(ref this.connecting);

    /// <summary>A bot reached play <paramref name="elapsed"/> after connecting.</summary>
    public void Joined(TimeSpan elapsed)
    {
        Interlocked.Decrement(ref this.connecting);
        Interlocked.Increment(ref this.playing);
        Interlocked.Increment(ref this.joinsTotal);
        Interlocked.Add(ref this.joinMillisecondsTotal, (long)elapsed.TotalMilliseconds);
    }

    /// <summary>A bot received the chunks around its spawn <paramref name="elapsed"/> after reaching play.</summary>
    public void SpawnAreaLoaded(TimeSpan elapsed)
    {
        Interlocked.Increment(ref this.spawnAreasLoaded);
        Interlocked.Add(ref this.spawnAreaMillisecondsTotal, (long)elapsed.TotalMilliseconds);
    }

    public void Disconnected(bool wasPlaying, string reason)
    {
        Interlocked.Decrement(ref wasPlaying ? ref this.playing : ref this.connecting);
        lock (this.gate)
            this.disconnectReasons[reason] = this.disconnectReasons.GetValueOrDefault(reason) + 1;
    }

    public void ObserveGameTime(string dimension, long gameTime)
    {
        lock (this.gate)
        {
            if (!this.latestTimes.TryGetValue(dimension, out var latest) || gameTime > latest.GameTime)
                this.latestTimes[dimension] = (gameTime, Stopwatch.GetTimestamp());
        }
    }

    /// <summary>
    /// Describes the interval since the last report: bots playing, chunk and byte rates, joins, and each dimension's tick
    /// rate as the bots saw it, from how far the game time the server sent them advanced.
    /// </summary>
    public string Report()
    {
        var now = Stopwatch.GetTimestamp();
        var seconds = Stopwatch.GetElapsedTime(this.lastReport, now).TotalSeconds;
        this.lastReport = now;

        var chunks = Interlocked.Exchange(ref this.chunks, 0);
        var bytes = Interlocked.Exchange(ref this.receivedBytes, 0);
        var joins = Volatile.Read(ref this.joinsTotal);
        var spawnAreas = Volatile.Read(ref this.spawnAreasLoaded);

        var text = $"{this.Playing} playing, {Volatile.Read(ref this.connecting)} connecting | "
            + $"{chunks / seconds:0} chunks/s, {bytes / seconds / 1024 / 1024:0.00} MiB/s | "
            + $"join avg {(joins == 0 ? 0 : Volatile.Read(ref this.joinMillisecondsTotal) / joins)} ms, "
            + $"spawn area avg {(spawnAreas == 0 ? 0 : Volatile.Read(ref this.spawnAreaMillisecondsTotal) / spawnAreas)} ms";

        lock (this.gate)
        {
            foreach (var (dimension, latest) in this.latestTimes)
            {
                if (this.windowStartTimes.TryGetValue(dimension, out var start) && latest.Timestamp > start.Timestamp)
                {
                    var tps = (latest.GameTime - start.GameTime) / Stopwatch.GetElapsedTime(start.Timestamp, latest.Timestamp).TotalSeconds;
                    text += $" | {dimension} {tps:0.0} TPS";
                }

                this.windowStartTimes[dimension] = latest;
            }

            if (this.disconnectReasons.Count > 0)
            {
                text += " | disconnects: " + string.Join(", ", this.disconnectReasons.Select(reason => $"{reason.Value}x {reason.Key}"));
                this.disconnectReasons.Clear();
            }
        }

        return text;
    }
}
