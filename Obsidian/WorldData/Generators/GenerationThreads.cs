using System.Collections.Concurrent;
using Obsidian.WorldData.Generators.Density;
using System.Threading;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// The threads chunk generation runs on: dedicated ones below normal priority, so generating never takes the thread
/// pool from the network and the levels' ticks, which run on it, and the operating system runs those first when cores
/// are short. One logical processor is left to them, and no more threads run than there are physical cores: a core's
/// second hardware thread (SMT) adds CPU time per chunk without generating any faster.
/// </summary>
/// <remarks>
/// Work started here continues here after awaiting, since awaits resume on the scheduler they ran on, unless awaited
/// with <c>ConfigureAwait(false)</c>. Generation must not block one of these threads waiting for work queued to them.
/// </remarks>
internal sealed class GenerationThreads : TaskScheduler
{
    /// <summary>
    /// The threads every level generates on.
    /// </summary>
    public static GenerationThreads Shared { get; } = new(ThreadCount(Environment.ProcessorCount, PhysicalCores.Count()));

    // How long a thread waits for work before it drops its scratch buffers (see Work). A constant, since Shared starts
    // the threads before later static fields are initialized.
    private const int IdleTrimMilliseconds = 10_000;

    [ThreadStatic]
    private static bool isGenerationThread;

    private readonly BlockingCollection<Task> queue = [];
    private readonly int threadCount;

    private GenerationThreads(int threadCount)
    {
        this.threadCount = threadCount;

        // UnsafeStart doesn't capture the caller's execution context: these threads live for the whole process, so a
        // captured context would keep whatever AsyncLocal values the first caller had, a plugin's objects included,
        // alive forever and stop that plugin unloading.
        for (var i = 0; i < threadCount; i++)
        {
            new Thread(this.Work)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = $"Chunk generation {i + 1}"
            }.UnsafeStart();
        }
    }

    public override int MaximumConcurrencyLevel => this.threadCount;

    /// <summary>
    /// How many generation threads run: one per logical processor but one, and at most one per physical core, when
    /// <paramref name="physicalCores"/> is known (see <see cref="PhysicalCores.Count"/>).
    /// </summary>
    internal static int ThreadCount(int processorCount, int? physicalCores)
    {
        var logical = Math.Max(1, processorCount - 1);
        if (physicalCores is not int cores)
            return logical;

        return Math.Clamp(cores, 1, logical);
    }

    /// <summary>
    /// Runs <paramref name="work"/> on the generation threads, its continuations too.
    /// </summary>
    public static Task Run(Func<Task> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Shared).Unwrap();

    /// <inheritdoc cref="Run(Func{Task})"/>
    public static Task Run(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Shared);

    protected override void QueueTask(Task task) => this.queue.Add(task);

    // Only the generation threads run tasks inline, so work never moves onto a caller's thread.
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) =>
        isGenerationThread && this.TryExecuteTask(task);

    protected override IEnumerable<Task> GetScheduledTasks() => this.queue.ToArray();

    private void Work()
    {
        isGenerationThread = true;

        while (!this.queue.IsCompleted)
        {
            if (!this.queue.TryTake(out var task, IdleTrimMilliseconds))
            {
                // Out of work for a while: give back the density scratch, which a busy overworld thread keeps a few
                // megabytes of, rather than hold it for the whole process.
                ScratchStacks.TrimCurrentThread();

                if (!this.queue.TryTake(out task, Timeout.Infinite))
                    break;
            }

            this.TryExecuteTask(task);
        }
    }
}
