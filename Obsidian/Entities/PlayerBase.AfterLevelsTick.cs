using Obsidian.WorldData.Maps;
using System.Threading;

namespace Obsidian.Entities;

public partial class PlayerBase
{
    // Held while the player ticks, grants advancements or saves. Leaving takes it once and closes ticks and grants,
    // so completion and its rewards cannot be separated by a save or applied after the final leave save.
    private readonly SemaphoreSlim tickGate = new(1, 1);

    // The player whose tick, grant or save owns the gate in the current flow.
    private static readonly AsyncLocal<PlayerBase?> tickingPlayer = new();

    private bool stoppedTicking;

    /// <summary>
    /// Whether the current flow owns the player's tick gate, which a leave waits for: a leave started from it (a
    /// plugin kicking the player during a tick or reward, say) mustn't be waited for there.
    /// </summary>
    internal bool IsInOwnTick => ReferenceEquals(tickingPlayer.Value, this);

    /// <summary>
    /// Waits for the player's current tick, grant or save and closes ticks and grants: they're leaving. Never call
    /// it from a flow that already owns the player's tick gate.
    /// </summary>
    internal async Task StopTickingAsync()
    {
        await this.tickGate.WaitAsync();
        this.stoppedTicking = true;
        this.tickGate.Release();
    }

    async ValueTask IPlayer.TickAfterLevelsAsync() => await this.RunTickAsync(this.TickAfterLevelsAsync);

    private async ValueTask TickAfterLevelsAsync()
    {
        await this.TickPortalsAsync();
        await this.PickupNearbyItemsAsync();
        await this.TickMenuAsync();
        await Obsidian.Events.MainEventHandler.RefreshStaleWindowAsync(this);
        await MapStorage.For(this.Level).TickAsync(this);
        await this.ProgressAdvancements.FlushAsync();
    }

    // Serializes player state with leaving. Nested grants (recipe rewards) and saves reuse their owning tick.
    private async ValueTask RunTickAsync(Func<ValueTask> tick, bool allowStopped = false)
    {
        if (this.IsInOwnTick)
        {
            if (!this.stoppedTicking || allowStopped)
                await tick();

            return;
        }

        await this.tickGate.WaitAsync();
        var previous = tickingPlayer.Value;
        tickingPlayer.Value = this;

        try
        {
            if (!this.stoppedTicking || allowStopped)
                await tick();
        }
        finally
        {
            tickingPlayer.Value = previous;
            this.tickGate.Release();
        }
    }
}
