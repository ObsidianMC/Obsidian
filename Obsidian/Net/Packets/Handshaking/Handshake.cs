using Microsoft.Extensions.Logging;
using Obsidian.API.Utilities;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Handshaking;

public partial class Handshake : IClientboundPacket, IServerboundPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public ProtocolVersion Version { get; private set; }

    [Field(1)]
    public string ServerAddress { get; private set; }

    [Field(2)]
    public ushort ServerPort { get; private set; }

    [Field(3), ActualType(typeof(int)), VarLength]
    public ClientState NextState { get; private set; }

    public int Id => 0x00;

    public ValueTask HandleAsync(Server server, Player player) => default;

    public async ValueTask HandleAsync(Client client)
    {
        var nextState = this.NextState;

        if (nextState == ClientState.Login)
        {
            if ((int)this.Version > (int)Server.DefaultProtocol)
            {
                await client.DisconnectAsync($"Outdated server! I'm still on {Server.DefaultProtocol.GetDescription()}.");

                return;
            }
            else if ((int)this.Version < (int)Server.DefaultProtocol)
            {
                await client.DisconnectAsync($"Outdated client! Please use {Server.DefaultProtocol.GetDescription()}.");

                return;
            }
        }
        else if (nextState is not ClientState.Status or ClientState.Login or ClientState.Handshaking)
        {
            client.Logger.LogError("Client sent unexpected state ({ClientState}), forcing it to disconnect.", nextState);
            await client.DisconnectAsync($"Invalid client state! Expected Status or Login, received {nextState}.");

            return;
        }

        client.SetState(nextState == ClientState.Login && this.Version != Server.DefaultProtocol ? ClientState.Closed : nextState);
        var versionDesc = this.Version.GetDescription();
        if (versionDesc is null)
            return;//No need to log if version description is null

        client.Logger.LogInformation("Handshaking with client (protocol: {VersionDescription} [{Version}], server: {ServerAddress}:{ServerPort})",
            versionDesc,
            this.Version,
            this.ServerAddress, this.ServerPort);
    }
}
