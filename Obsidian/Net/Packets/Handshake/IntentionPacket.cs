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
            if (this.Version != ServerConstants.DefaultProtocol)
                Log.ProtocolMismatch(client.Logger, (int)this.Version, (int)ServerConstants.DefaultProtocol);

            if ((int)this.Version > (int)ServerConstants.DefaultProtocol)
            {
                await client.DisconnectAsync($"Outdated server! I'm still on {ServerConstants.DefaultProtocol.GetDescription()}.");
            }
            else if ((int)this.Version < (int)ServerConstants.DefaultProtocol)
            {
                await client.DisconnectAsync($"Outdated client! Please use {ServerConstants.DefaultProtocol.GetDescription()}.");
            }
        }
        else if (nextState is not ClientState.Status)
        {
            Log.UnexpectedState(client.Logger, nextState);
            await client.DisconnectAsync($"Invalid client state! Expected Status or Login, received {nextState}.");
        }

        client.SetState(nextState);

        Log.Handshake(client.Logger, (int)this.Version, this.ServerAddress, this.ServerPort, nextState);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnecting client with protocol {ClientProtocol}; the server uses protocol {ServerProtocol}")]
        public static partial void ProtocolMismatch(ILogger logger, int clientProtocol, int serverProtocol);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnecting client that requested unexpected state {ClientState}")]
        public static partial void UnexpectedState(ILogger logger, ClientState clientState);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Handshake with protocol {Protocol} to {ServerAddress}:{ServerPort}, next state {NextState}")]
        public static partial void Handshake(ILogger logger, int protocol, string serverAddress, ushort serverPort, ClientState nextState);
    }
}
