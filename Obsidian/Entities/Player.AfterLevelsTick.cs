using Obsidian.WorldData.Maps;

namespace Obsidian.Entities;

public partial class Player
{
    async ValueTask IPlayer.TickAfterLevelsAsync()
    {
        // The tick took its players before this one left (going back through configuration, say), after which
        // picking up items would put them in an inventory that was already saved.
        if (!this.Level.Players.TryGetValue(this.Uuid, out var current) || current != this)
            return;

        await this.TickPortalsAsync();
        await this.PickupNearbyItemsAsync();
        await MapStorage.For(this.Level).TickAsync(this);
    }
}
