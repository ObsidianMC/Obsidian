using Obsidian.WorldData.Maps;

namespace Obsidian.Entities;

public partial class Player
{
    async ValueTask IPlayer.TickAfterLevelsAsync()
    {
        await this.TickPortalsAsync();
        await this.PickupNearbyItemsAsync();
        await MapStorage.For(this.Level).TickAsync(this);
    }
}
