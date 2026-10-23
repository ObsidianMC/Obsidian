using System.Threading;

namespace Obsidian.Utilities;

/// <summary>
/// Collects the durations of a recurring task, and optionally of its stages, until <see cref="Take"/> reads and resets
/// them. Used for the tick statistics the server logs (see <c>Server.ReportTickStats</c>). Recording and taking may run
/// on different threads.
/// </summary>
internal sealed class TimingWindow
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, double> stageTotals = [];
    private double total;
    private double max;
    private int count;

    /// <summary>Records one run that took <paramref name="milliseconds"/>, with the time each of its stages took.</summary>
    public void Record(double milliseconds, IEnumerable<(string Stage, double Milliseconds)>? stages = null)
    {
        lock (this.gate)
        {
            this.count++;
            this.total += milliseconds;
            this.max = Math.Max(this.max, milliseconds);

            if (stages is null)
                return;

            foreach (var (stage, stageMilliseconds) in stages)
                this.stageTotals[stage] = this.stageTotals.GetValueOrDefault(stage) + stageMilliseconds;
        }
    }

    /// <summary>Returns what was recorded since the last call, and starts over.</summary>
    public Summary Take()
    {
        lock (this.gate)
        {
            var count = this.count;
            var stages = this.stageTotals
                .Select(stage => (stage.Key, count == 0 ? 0 : stage.Value / count))
                .OrderByDescending(stage => stage.Item2)
                .ToArray();
            var summary = new Summary(count, count == 0 ? 0 : this.total / count, this.max, stages);

            this.stageTotals.Clear();
            this.total = this.max = 0;
            this.count = 0;
            return summary;
        }
    }

    /// <param name="Count">How many runs were recorded.</param>
    /// <param name="Average">Their mean duration, in milliseconds.</param>
    /// <param name="Max">The longest one, in milliseconds.</param>
    /// <param name="Stages">Each stage's mean time per run, slowest first.</param>
    public readonly record struct Summary(int Count, double Average, double Max, (string Stage, double Average)[] Stages)
    {
        /// <summary>For example "avg 12.3 ms, max 40.1 ms over 200 [entities 4.1, tracking 3.0]".</summary>
        public string Describe(int stageCount = 0)
        {
            var text = $"avg {this.Average:0.00} ms, max {this.Max:0.0} ms over {this.Count}";
            var stages = this.Stages.Where(stage => stage.Average >= 0.05).Take(stageCount).ToArray();
            return stages.Length == 0
                ? text
                : $"{text} [{string.Join(", ", stages.Select(stage => $"{stage.Stage} {stage.Average:0.00}"))}]";
        }
    }
}
