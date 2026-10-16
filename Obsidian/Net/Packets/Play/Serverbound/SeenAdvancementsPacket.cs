namespace Obsidian.Net.Packets.Play.Serverbound;

/// <summary>Vanilla's open-tab action carries an identifier; closing the screen carries only the action.</summary>
public partial class SeenAdvancementsPacket
{
    public int Action { get; private set; }
    public string? Tab { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Action = reader.ReadVarInt();
        this.Tab = this.Action == 0 ? reader.ReadString() : null;
    }

    public override async ValueTask HandleAsync(IServer server, IClientPlayer player)
    {
        if (this.Action == 0 && this.Tab is string tab
            && AdvancementsRegistry.Current.TryGetValue(tab, out var advancement)
            && advancement.Parent.Length == 0 && advancement.Display is not null)
        {
            await player.QueuePacketAsync(new Clientbound.SelectAdvancementsTabPacket { Tab = tab });
        }
    }
}
