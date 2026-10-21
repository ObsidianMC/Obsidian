using System.Collections.Concurrent;
using System.Threading;

namespace Obsidian.WorldData.Generators;

/// <summary>
/// The threads chunk generation runs on: dedicated ones below normal priority, so generating never takes the thread
/// pool from the network and the levels' ticks, which run on it, and the operating system runs those first when cores
/// are short. One core is left to them.
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
    public static GenerationThreads Shared { get; } = new(Math.Max(1, Environment.ProcessorCount - 1));

    [ThreadStatic]
    private static bool isGenerationThread;

    private readonly BlockingCollection<Task> queue = [];
    private readonly int threadCount;

    private GenerationThreads(int threadCount)
    {
        this.threadCount = threadCount;
        for (var i = 0; i < threadCount; i++)
        {
            new Thread(this.Work)
            {
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal,
                Name = $"Chunk generation {i + 1}"
            }.Start();
        }
    }

    public override int MaximumConcurrencyLevel => this.threadCount;

    /// <summary>
    /// Runs <paramref name="work"/> on the generation threads, its continuations too.
    /// </summary>
    public static Task Run(Func<Task> work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Shared).Unwrap();

    /// <inheritdoc cref="Run(Func{Task})"/>
    public static Task Run(Action work) =>
        Task.Factory.StartNew(work, CancellationToken.None, TaskCreationOptions.DenyChildAttach, Shared);

    /// <summary>
    /// Options running parallel loops on the generation threads, as many at once as there are threads.
    /// </summary>
    public static ParallelOptions Parallel(CancellationToken cancellationToken = default) => new()
    {
        TaskScheduler = Shared,
        MaxDegreeOfParallelism = Shared.threadCount,
        CancellationToken = cancellationToken
    };

    protected override void QueueTask(Task task) => this.queue.Add(task);

    // Only the generation threads run tasks inline, so work never moves onto a caller's thread.
    protected override bool TryExecuteTaskInline(Task task, bool taskWasPreviouslyQueued) =>
        isGenerationThread && this.TryExecuteTask(task);

    protected override IEnumerable<Task> GetScheduledTasks() => this.queue.ToArray();

    private void Work()
    {
        isGenerationThread = true;
        foreach (var task in this.queue.GetConsumingEnumerable())
            this.TryExecuteTask(task);
    }
}
