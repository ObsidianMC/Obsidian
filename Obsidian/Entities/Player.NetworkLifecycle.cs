using Obsidian.WorldData.Maps;

namespace Obsidian.Entities;

public partial class Player
{
    public async ValueTask TickNetworkLifecycleAsync()
    {
        await this.TickPortalsAsync();
        await this.PickupNearbyItemsAsync();
        await MapStorage.For(this.Level).TickAsync(this);
    }
}
