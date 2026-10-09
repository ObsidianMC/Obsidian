using System;
using System.Diagnostics;
using System.Threading;

namespace WorldgenBench;

/// <summary>
/// Totals each stage's time, calls and allocations. Safe to use from several threads; allocations are those of the calling
/// thread.
/// </summary>
internal sealed class StageTimer<TStage> where TStage : struct, Enum
{
    private readonly long[] ticks = new long[Enum.GetValues<TStage>().Length];
    private readonly long[] bytes = new long[Enum.GetValues<TStage>().Length];
    private readonly int[] calls = new int[Enum.GetValues<TStage>().Length];

    public void Time(TStage stage, Action action)
    {
        var index = Convert.ToInt32(stage);
        var allocated = GC.GetAllocatedBytesForCurrentThread();
        var start = Stopwatch.GetTimestamp();
        action();
        Interlocked.Add(ref this.ticks[index], Stopwatch.GetTimestamp() - start);
        Interlocked.Add(ref this.bytes[index], GC.GetAllocatedBytesForCurrentThread() - allocated);
        Interlocked.Increment(ref this.calls[index]);
    }

    /// <summary>
    /// Prints a row per stage, with its time per call and per output chunk.
    /// </summary>
    public void Print(int outputChunks)
    {
        Console.WriteLine("stage             calls   total ms   ms/call  ms/chunk   alloc MB   KB/call");
        long totalTicks = 0, totalBytes = 0;
        foreach (var stage in Enum.GetValues<TStage>())
        {
            var i = Convert.ToInt32(stage);
            var ms = this.ticks[i] * 1000.0 / Stopwatch.Frequency;
            var calls = Math.Max(1, this.calls[i]);
            Console.WriteLine($"{stage,-16} {this.calls[i],6} {ms,10:F0} {ms / calls,9:F3} {ms / outputChunks,9:F3} " +
                $"{this.bytes[i] / 1048576.0,10:F0} {this.bytes[i] / 1024.0 / calls,9:F0}");
            totalTicks += this.ticks[i];
            totalBytes += this.bytes[i];
        }

        var totalMs = totalTicks * 1000.0 / Stopwatch.Frequency;
        Console.WriteLine($"{"total",-16} {"",6} {totalMs,10:F0} {"",9} {totalMs / outputChunks,9:F3} {totalBytes / 1048576.0,10:F0} " +
            $"{totalBytes / 1024.0 / outputChunks,9:F0} per chunk");
    }
}
