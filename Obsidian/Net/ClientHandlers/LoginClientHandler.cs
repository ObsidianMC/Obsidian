using Microsoft.Extensions.Logging;
using Obsidian.Integrated;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Login.Serverbound;
using System.Threading;

namespace Obsidian.Net.ClientHandlers;
internal sealed partial class LoginClientHandler : ClientHandler
{
    // How long the local player has to answer the join query.
    private static readonly TimeSpan JoinQueryTimeout = TimeSpan.FromSeconds(10);

    // The id of the join query waiting for its answer, or 0 for none.
    private int pendingJoinQuery;

    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, buffer) = packetData;

        switch (id)
        {
            case HelloPacket.PacketId:
                {
                    if (await this.Server.ShouldThrottleAsync(this.Client))
                        return false;

                    try
                    {
                        await this.HandleLoginStartAsync(buffer.GetBuffer());
                    }
                    catch
                    {
                        return false;
                    }

                    return true;
                }
            case KeyPacket.PacketId:
                {
                    try
                    {
                        await this.HandleEncryptionResponseAsync(buffer.GetBuffer());
                    }
                    catch
                    {
                        return false;
                    }

                    return true;
                }
            case CustomQueryAnswerPacket.PacketId:
                {
                    try
                    {
                        await this.HandleCustomQueryAnswerAsync(buffer.GetBuffer());
                    }
                    catch
                    {
                        return false;
                    }

                    return true;
                }
            case LoginAcknowledgedPacket.PacketId:
                {
                    this.Client.SetState(ClientState.Configuration);

                    // The rest of configuration follows once the client answered the handshake's ping.
                    this.Client.StartConfiguration();

                    return true;
                }
            default:
                Log.UnknownPacket(this.Logger, id);
                await this.Client.DisconnectAsync("Unknown Packet Id.");
                break;
        }

        return false;
    }

    private async Task HandleLoginStartAsync(byte[] data)
    {
        var loginStart = HelloPacket.Deserialize(data);

        var username = this.Server.Configuration.Network.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;
        var world = this.Server.DefaultWorld;

        Log.LoginRequest(this.Logger, username);

        // On an integrated server, the local player's name is reserved for the client that knows the join secret, and
        // nobody else may join until the world is opened to LAN.
        if (this.Server.Integrated is IntegratedSession integrated)
        {
            if (integrated.IsLocalPlayer(username))
            {
                this.SendJoinQuery();
                return;
            }

            if (!integrated.IsPublished)
            {
                await this.Client.DisconnectAsync("This world isn't open to LAN.");
                return;
            }
        }

        await this.Server.DisconnectPlayerIfConnectedAsync(username);

        // The earlier connection's leave may still be saving the player (it waits for their tick under way).
        if (this.Server is Server server)
            await server.WaitForLeavesAsync(username);

        if (this.Server.Configuration.OnlineMode)
        {
            if (await this.Client.TrySetCachedProfileAsync(username))
                this.Client.Initialize(world);

            return;
        }

        if (this.Server is Server { Bans: var bans } && bans.GetBan(Guid.Empty, username) is BannedPlayer ban)
        {
            await this.Client.DisconnectAsync(BanList.DisconnectMessage(ban.Reason));
        }
        else if (this.Server.Configuration.Whitelist && !this.Server.IsWhitelisted(username))
        {
            await this.Client.DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
        }
        else
        {
            this.Client.InitializeOffline(username, world);
        }
    }

    /// <summary>
    /// Asks the client claiming to be the integrated server's local player for the join secret, and disconnects it if
    /// the answer doesn't come in time.
    /// </summary>
    private void SendJoinQuery()
    {
        var queryId = Globals.Random.Next(1, int.MaxValue);
        Volatile.Write(ref this.pendingJoinQuery, queryId);

        this.SendPacket(new Packets.Login.Clientbound.CustomQueryPacket
        {
            MessageId = queryId,
            Channel = IntegratedSession.JoinQueryChannel,
            Payload = ReadOnlyMemory<byte>.Empty
        });

        _ = Task.Delay(JoinQueryTimeout).ContinueWith(async _ =>
        {
            if (Interlocked.CompareExchange(ref this.pendingJoinQuery, 0, queryId) == queryId)
                await this.Client.DisconnectAsync("Timed out joining the world.");
        }, TaskScheduler.Default);
    }

    /// <summary>
    /// Logs the local player in once they answered the join query with the right secret: with the configured UUID and
    /// without Mojang authentication, even in online mode. Any other answer disconnects them.
    /// </summary>
    private async Task HandleCustomQueryAnswerAsync(byte[] data)
    {
        var answer = CustomQueryAnswerPacket.Deserialize(data);
        var integrated = this.Server.Integrated;

        var expected = Interlocked.Exchange(ref this.pendingJoinQuery, 0);
        if (integrated is null || expected == 0 || answer.MessageId != expected)
        {
            await this.Client.DisconnectAsync("Unexpected login query answer.");
            return;
        }

        if (!answer.Successful || !integrated.VerifyJoinSecret(answer.Data.Span))
        {
            Log.JoinSecretRejected(this.Logger);
            await this.Client.DisconnectAsync("Only the world's owner can join as this player.");
            return;
        }

        var name = integrated.Configuration.LocalPlayerName!;

        // Like vanilla's integrated player list, a second connection as the local player is refused.
        if (this.Server.IsPlayerOnline(name))
        {
            await this.Client.DisconnectAsync(new ChatMessage { Translate = "multiplayer.disconnect.name_taken" });
            return;
        }

        // The local player's earlier connection may still be saving them as it leaves.
        if (this.Server is Server server)
            await server.WaitForLeavesAsync(name);

        this.Client.InitializeOffline(name, this.Server.DefaultWorld, integrated.Configuration.LocalPlayerUuid);
    }

    private async Task HandleEncryptionResponseAsync(byte[] data)
    {
        this.Client.ThrowIfInvalidEncryptionRequest();

        // Decrypt the shared secret and verify the token
        await KeyPacket.Deserialize(data).HandleAsync(this.Client);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning, Message = "Disconnecting client that sent unknown login packet {PacketId}")]
        public static partial void UnknownPacket(ILogger logger, int packetId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Login request from {Username}")]
        public static partial void LoginRequest(ILogger logger, string username);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Rejected a login as the local player: wrong or missing join secret")]
        public static partial void JoinSecretRejected(ILogger logger);
    }
}
