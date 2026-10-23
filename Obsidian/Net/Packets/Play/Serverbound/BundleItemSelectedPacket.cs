namespace Obsidian.Net.Packets.Play.Serverbound;

public partial class BundleItemSelectedPacket
{
    public override ValueTask HandleAsync(IServer server, IClientPlayer player) =>
        Obsidian.Events.MainEventHandler.SelectBundleItemAsync(player, this.SlotId, this.SelectedItemIndex);
}
