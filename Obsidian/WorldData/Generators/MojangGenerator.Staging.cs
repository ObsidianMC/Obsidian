using System.Runtime.ExceptionServices;
using System.Threading;

namespace Obsidian.WorldData.Generators;

internal partial class MojangGenerator
{
    /// <summary>
    /// Generates <paramref name="chunks"/> up to their light, along with the neighbors they need, staged so that steps
    /// running at once never wait on each other's locks. Completing the chunks afterwards only marks them complete.
    /// Pregeneration stages its area a tile at a time.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Every step is a node of a dependency graph, and each starts as soon as the steps it needs are done, on as many
    /// generation threads as there are. Decorating a chunk needs its neighbors carved; post-processing it needs every
    /// decoration that writes into its neighbors done (those of chunks within 2 of it); lighting it needs the same of
    /// post-processing. Two steps of the same kind whose 3 by 3 areas overlap (chunks within 2 of each other) run in a
    /// fixed order: that of a 3 by 3 colouring of world positions, the lower colour first. The order of every pair of
    /// steps touching the same chunks is then that of running each kind in 9 waves, one colour after another, so the
    /// result is the same, without waiting for the slowest step of each wave. Steps whose areas don't overlap touch
    /// different chunks, so their order doesn't matter.
    /// </para>
    /// <para>
    /// Ready steps run latest kind first, then in order along the tile, so the later kinds follow right behind the
    /// carving and little is left once it's done; on a single generation thread, earliest kind first, as nothing waits
    /// at the end there. Chunks done already are skipped. Cancelling stops new steps from starting; steps already
    /// running finish.
    /// </para>
    /// </remarks>
    internal async Task PrepareAsync(IReadOnlyCollection<(int X, int Z)> chunks, CancellationToken cancellationToken = default)
    {
        // Completing a chunk decorates its neighbors, and decorating a chunk carves its neighbors.
        var decorated = Around(chunks);
        var carved = Around(decorated);

        var graph = new StagingGraph(cancellationToken);
        graph.AddNodes(StagingStep.Carve, carved, position => this.CarveAsync(position.X, position.Z));
        graph.AddNodes(StagingStep.Decorate, decorated, position => this.DecorateAsync(position.X, position.Z));
        graph.AddNodes(StagingStep.Finish, chunks, async position =>
            await this.FinishBlocksAsync(await this.GetChunkAsync(position.X, position.Z)));
        graph.AddNodes(StagingStep.Light, chunks, async position =>
            await this.LightAsync(await this.GetChunkAsync(position.X, position.Z)));

        await graph.RunAsync(GenerationThreads.Shared.MaximumConcurrencyLevel);

        // The chunks and their neighbors.
        static HashSet<(int X, int Z)> Around(IEnumerable<(int X, int Z)> chunks)
        {
            var around = new HashSet<(int X, int Z)>();
            foreach (var (x, z) in chunks)
            {
                for (var dx = -1; dx <= 1; dx++)
                {
                    for (var dz = -1; dz <= 1; dz++)
                        around.Add((x + dx, z + dz));
                }
            }

            return around;
        }
    }

    /// <summary>
    /// The kinds of steps staging runs, in the order a chunk goes through them.
    /// </summary>
    private enum StagingStep
    {
        Carve,
        Decorate,
        Finish,
        Light
    }

    /// <summary>
    /// The steps of a staging (see <see cref="PrepareAsync"/>) and what each needs done first, run as their needs are met.
    /// </summary>
    private sealed class StagingGraph(CancellationToken cancellationToken)
    {
        private readonly Dictionary<(StagingStep Step, int X, int Z), Node> nodes = [];
        private readonly PriorityQueue<Node, (int Step, int X, int Z)> ready = new();
        private readonly SemaphoreSlim available = new(0);
        private readonly Lock sync = new();

        private int workers;
        private int remaining;
        private bool stopping;
        private ExceptionDispatchInfo? failure;

        /// <summary>
        /// Adds a step of kind <paramref name="step"/> for each of <paramref name="positions"/>, after the kinds before it.
        /// </summary>
        public void AddNodes(StagingStep step, IEnumerable<(int X, int Z)> positions, Func<(int X, int Z), Task> run)
        {
            foreach (var position in positions)
                this.nodes.Add((step, position.X, position.Z), new Node(step, position, run));

            foreach (var position in positions)
            {
                var node = this.nodes[(step, position.X, position.Z)];
                if (step == StagingStep.Carve)
                    continue;

                // Decorating needs the area carved; the later steps need every step of the kind before them whose area
                // overlaps theirs done.
                var before = step - 1;
                var reach = step == StagingStep.Decorate ? 1 : 2;
                this.AddNeeds(node, before, reach, position, _ => true);

                // Overlapping steps of the same kind run lower colour first.
                var colour = Colour(position);
                this.AddNeeds(node, step, 2, position, other => Colour(other) < colour);
            }
        }

        /// <summary>
        /// Runs every step on up to <paramref name="workerCount"/> generation threads at once, and returns once they're
        /// all done. Throws the first step's failure, or <see cref="OperationCanceledException"/> once cancelled, after
        /// the steps already running finish.
        /// </summary>
        public async Task RunAsync(int workerCount)
        {
            this.remaining = this.nodes.Count;
            this.workers = workerCount;
            foreach (var node in this.nodes.Values)
            {
                if (node.Needs == 0)
                    this.ready.Enqueue(node, Priority(node));
            }

            if (this.remaining == 0)
                return;

            this.available.Release(this.ready.Count);

            var working = new Task[workerCount];
            for (var i = 0; i < working.Length; i++)
                working[i] = GenerationThreads.Run(this.WorkAsync);

            await Task.WhenAll(working);

            this.failure?.Throw();
            cancellationToken.ThrowIfCancellationRequested();
        }

        private void AddNeeds(Node node, StagingStep step, int reach, (int X, int Z) position,
            Func<(int X, int Z), bool> include)
        {
            for (var dx = -reach; dx <= reach; dx++)
            {
                for (var dz = -reach; dz <= reach; dz++)
                {
                    (int X, int Z) other = (position.X + dx, position.Z + dz);
                    if (other == position && step == node.Step)
                        continue;

                    if (!include(other) || !this.nodes.TryGetValue((step, other.X, other.Z), out var needed))
                        continue;

                    needed.Dependents.Add(node);
                    node.Needs++;
                }
            }
        }

        private async Task WorkAsync()
        {
            while (true)
            {
                await this.available.WaitAsync();

                Node? node;
                lock (this.sync)
                {
                    if (cancellationToken.IsCancellationRequested)
                        this.Stop();

                    if (this.stopping || !this.ready.TryDequeue(out node, out _))
                        return;
                }

                try
                {
                    await node.Run(node.Position);
                }
                catch (Exception ex)
                {
                    lock (this.sync)
                    {
                        this.failure ??= ExceptionDispatchInfo.Capture(ex);
                        this.Stop();
                    }
                }

                this.Complete(node);
            }
        }

        // Counts a step done, and readies the steps that were only waiting on it.
        private void Complete(Node node)
        {
            var released = 0;
            lock (this.sync)
            {
                this.remaining--;

                if (cancellationToken.IsCancellationRequested)
                    this.Stop();

                if (!this.stopping)
                {
                    foreach (var dependent in node.Dependents)
                    {
                        if (--dependent.Needs > 0)
                            continue;

                        this.ready.Enqueue(dependent, Priority(dependent));
                        released++;
                    }
                }

                // Every step done: the workers waiting for another wake up to leave.
                if (this.remaining == 0)
                    this.Stop();
            }

            if (released > 0)
                this.available.Release(released);
        }

        // Stops readying steps, and wakes every worker so they leave once their step is done. Called under the lock.
        private void Stop()
        {
            if (this.stopping)
                return;

            this.stopping = true;
            this.available.Release(this.workers);
        }

        // With several workers, the latest kind first, then along the tile, so a chunk's later steps follow its carving
        // closely and little is left for the last workers once carving ends. A single worker leaves no thread idle at
        // the end, so it runs the steps of each kind back to back instead, which keeps their data warm in the caches.
        private (int Step, int X, int Z) Priority(Node node)
        {
            var step = this.workers > 1 ? -(int)node.Step : (int)node.Step;

            return (step, node.Position.X, node.Position.Z);
        }

        // The class of a 3 by 3 colouring of world positions: positions of the same class are at least 3 apart.
        private static int Colour((int X, int Z) position) => Mod3(position.X) * 3 + Mod3(position.Z);

        private static int Mod3(int value) => (value % 3 + 3) % 3;

        private sealed class Node(StagingStep step, (int X, int Z) position, Func<(int X, int Z), Task> run)
        {
            public StagingStep Step { get; } = step;

            public (int X, int Z) Position { get; } = position;

            public Func<(int X, int Z), Task> Run { get; } = run;

            // The steps waiting on this one.
            public List<Node> Dependents { get; } = [];

            // How many steps this one still waits on.
            public int Needs { get; set; }
        }
    }
}
