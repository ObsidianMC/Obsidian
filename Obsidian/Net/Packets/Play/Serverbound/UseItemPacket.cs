using Obsidian.API.Events;
using Obsidian.API.Inventory;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Serverbound;
public partial class UseItemPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public InteractionHand Hand { get; private set; }


    [Field(1), VarLength]
    public int Sequence { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Hand = reader.ReadVarInt<InteractionHand>();
        this.Sequence = reader.ReadVarInt();
        reader.ReadSingle();
        reader.ReadSingle();
    }

    public async override ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        var args = new PlayerInteractEventArgs(player, server)
        {
            Item = this.Hand == InteractionHand.MainHand ? player.GetHeldItem() : player.GetOffHandItem(),
            Hand = this.Hand,
        };
        await server.EventDispatcher.ExecuteEventAsync(args);
        if (args.IsCancelled)
            return;

        if (args.Item is { Count: > 0 } stack && Plugins.ItemBehaviors.CanUseItems(player))
        {
            var used = await Plugins.ItemBehaviors.UseAsync(new ItemUseContext
            {
                Player = player,
                Stack = stack,
                Hand = this.Hand
            });

            if (used)
                return;
        }

        if (player is Obsidian.Entities.Player concrete)
            await concrete.StartUsingItemAsync(Hand);
    }
}
