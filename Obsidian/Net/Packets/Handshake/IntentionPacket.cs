using Microsoft.Extensions.Logging;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Handshake.Serverbound;

public partial class IntentionPacket
{
    [Field(0), ActualType(typeof(int)), VarLength]
    public ProtocolVersion Version { get; private set; }

    [Field(1)]
    public string ServerAddress { get; private set; } = default!;

    [Field(2)]
    public ushort ServerPort { get; private set; }

    [Field(3), ActualType(typeof(int)), VarLength]
    public ClientState NextState { get; private set; }

    public override void Populate(INetStreamReader reader)
    {
        this.Version = (ProtocolVersion)reader.ReadVarInt();
        this.ServerAddress = reader.ReadString();
        this.ServerPort = reader.ReadUnsignedShort();
        this.NextState = (ClientState)reader.ReadVarInt();
    }

    public async override ValueTask HandleAsync(IClient client)
    {
        var nextState = this.NextState;

        if (nextState == ClientState.Login)
        {
            if ((int)this.Version > (int)Server.DefaultProtocol)
            {
                await client.DisconnectAsync($"Outdated server! I'm still on {Server.DefaultProtocol.GetDescription()}.");
            }
            else if ((int)this.Version < (int)Server.DefaultProtocol)
            {
                await client.DisconnectAsync($"Outdated client! Please use {Server.DefaultProtocol.GetDescription()}.");
            }
        }
        else if (nextState is not ClientState.Status or ClientState.Login or ClientState.Handshaking)
        {
            client.Logger.LogWarning("Client sent unexpected state ({RedText}{ClientState}{WhiteText}), forcing it to disconnect.", ChatColor.Red, nextState, ChatColor.White);
            await client.DisconnectAsync($"Invalid client state! Expected Status or Login, received {nextState}.");
        }

        client.SetState(nextState);

        var versionDesc = this.Version.GetDescription();
        if (versionDesc is null)
            return;//No need to log if version description is null

        client.Logger.LogInformation("Handshaking with client (protocol: {YellowText}{VersionDescription}{WhiteText} [{YellowText}{Version}{WhiteText}], server: {YellowText}{ServerAddress}:{ServerPort}{WhiteText})", ChatColor.Yellow, versionDesc, ChatColor.White, ChatColor.Yellow, this.Version, ChatColor.White, ChatColor.Yellow, this.ServerAddress, this.ServerPort, ChatColor.White);
    }
}
