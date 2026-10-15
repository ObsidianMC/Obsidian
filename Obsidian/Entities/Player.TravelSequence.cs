using System.Threading;

namespace Obsidian.Entities;

public sealed partial class Player
{
    private readonly SemaphoreSlim travelSequences = new(1, 1);
    private bool travelSequenceRunning;

    /// <summary>
    /// Serializes travel through its packet drain, destination updates and callbacks. Requests from that flow
    /// queue instead of waiting on their own gate; independent callers wait for the complete sequence.
    /// </summary>
    private ValueTask RunTravelSequenceAsync(Func<ValueTask> travel) =>
        DeferredInventoryWork.RunOrDeferAsync(() => this.RunTravelUnderGateAsync(travel), this.Client);

    private async ValueTask RunTravelUnderGateAsync(Func<ValueTask> travel)
    {
        var callbacks = new DeferredInventoryWork(sequenceClient: this.Client);
        await this.travelSequences.WaitAsync();
        try
        {
            Volatile.Write(ref this.travelSequenceRunning, true);
            using (callbacks.Enter())
                await travel();
        }
        finally
        {
            Volatile.Write(ref this.travelSequenceRunning, false);
            this.travelSequences.Release();
            // Follow-up operations acquire the same gate, after all packets from this sequence were queued.
            // Drain even on failure, like inventory transactions; one failed follow-up cannot discard the rest.
            await callbacks.RunAsync(this.Logger);
        }
    }
}
