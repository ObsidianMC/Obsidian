using Obsidian.Entities;
using Obsidian.WorldData;

namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class UseItemOnPacket
{
    // Vanilla's MinecartItem.useOn: a minecart item used on a rail puts the cart on it; on anything else it does
    // nothing.
    private async ValueTask<UseResult> PlaceMinecartAsync(BlockUse use)
    {
        if (VehicleItems.MinecartFor(use.Item!.Holder) is not EntityType type)
            return UseResult.Pass;

        var cart = VehicleItems.PlaceMinecart(use.Level, type, this.Position, use.Block, use.Item);
        if (cart is null)
            return UseResult.Fail;

        use.Level.EmitGameEvent(MobGameEvent.EntityPlace, cart.Position, use.Player);
        await use.ConsumeItemAsync();

        return UseResult.Success;
    }
}
