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
        var graph = StagingGraph.For(chunks, this.RunStagingStepAsync, cancellationToken);
        await graph.RunAsync(GenerationThreads.Shared.MaximumConcurrencyLevel);
    }

    private async Task RunStagingStepAsync(StagingStep step, (int X, int Z) position)
    {
        switch (step)
        {
            case StagingStep.Carve:
                await this.CarveAsync(position.X, position.Z);
                break;
            case StagingStep.Decorate:
                await this.DecorateAsync(position.X, position.Z);
                break;
            case StagingStep.Finish:
                await this.FinishBlocksAsync(await this.GetChunkAsync(position.X, position.Z));
                break;
            case StagingStep.Light:
                await this.LightAsync(await this.GetChunkAsync(position.X, position.Z));
                break;
        }
    }
}
