namespace Obsidian.Net.Packets.Common;
public partial record class ClientInformationPacket
{
    public override ValueTask HandleAsync(IServer server, IPlayer player)
    {
        player.ClientInformation = Information with
        {
            ViewDistance = int.Min(Information.ViewDistance, server.Configuration.ViewDistance)
        };

        return default;
    }
}
