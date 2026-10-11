using Obsidian.WorldData.Maps;
using System.Threading;

namespace Obsidian.Entities;

public partial class Player
{
    // Held while the player ticks. Leaving takes it once and stops the ticks, so that what's saved is what the last
    // tick left: a tick that took its players before this one left would otherwise pick up items into an inventory
    // already saved.
    private readonly SemaphoreSlim tickGate = new(1, 1);

    // The player whose tick the current flow is in.
    private static readonly AsyncLocal<Player?> tickingPlayer = new();

    private bool stoppedTicking;

    /// <summary>
    /// Whether the current flow is in one of the player's ticks, which a leave waits for: a leave started from it (a
    /// plugin kicking the player, say) mustn't be waited for there.
    /// </summary>
    internal bool IsInOwnTick => tickingPlayer.Value == this;

    /// <summary>
    /// Waits for a tick of the player under way and stops ticking them: they're leaving. Never call it from the
    /// player's own tick.
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
        await MapStorage.For(this.Level).TickAsync(this);
    }

    // Runs one of the player's ticks unless they left.
    private async ValueTask RunTickAsync(Func<ValueTask> tick)
    {
        await this.tickGate.WaitAsync();
        tickingPlayer.Value = this;

        try
        {
            if (!this.stoppedTicking)
                await tick();
        }
        finally
        {
            tickingPlayer.Value = null;
            this.tickGate.Release();
        }
    }
}
