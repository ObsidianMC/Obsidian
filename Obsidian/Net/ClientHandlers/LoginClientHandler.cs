using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Configuration.Clientbound;
using Obsidian.Net.Packets.Login.Serverbound;
using Obsidian.WorldData;

namespace Obsidian.Net.ClientHandlers;
internal sealed class LoginClientHandler : ClientHandler
{
    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, buffer) = packetData;

        switch (id)
        {
            case 0x00:
                {
                    if (await this.Server.ShouldThrottleAsync(this.Client))
                        return false;

                    try
                    {
                        await this.HandleLoginStartAsync(buffer.Data);
                    }
                    catch { return false; }

                    return true;
                }
            case 0x01:
                {
                    try
                    {
                        await this.HandleEncryptionResponseAsync(buffer.Data);
                    }
                    catch { return false; }

                    return true;
                }
            case 0x02://plugin response
                break;
            case 0x03:
                {
                    this.Logger.LogDebug("Login Acknowledged switching to configuration state.");

                    this.Client.SetState(ClientState.Configuration);

                    return true;
                }
            default:
                this.Logger.LogError("Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                await this.Client.DisconnectAsync("Unknown Packet Id.");
                break;
        }

        return false;
    }

    private async Task HandleLoginStartAsync(byte[] data)
    {
        var loginStart = HelloPacket.Deserialize(data);

        var username = this.Server.Configuration.Network.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;
        var world = (World)this.Server.DefaultWorld;

        this.Logger.LogDebug("Received login request from user {Username}", username);
        await this.Server.DisconnectIfConnectedAsync(username);

        if (this.Server.Configuration.OnlineMode && await this.Client.TrySetCachedProfileAsync(username))
        {
            this.Client.Initialize(world);

            return;
        }

        if (this.Server.Configuration.Whitelist && !this.Server.IsWhitelisted(username))
        {
            await this.Client.DisconnectAsync("You are not whitelisted on this server\nContact server administrator");
        }
        else
        {
            this.Client.InitializeOffline(username, world);
        }
    }

    private async Task HandleEncryptionResponseAsync(byte[] data)
    {
        this.Client.ThrowIfInvalidEncryptionRequest();

        // Decrypt the shared secret and verify the token
        await KeyPacket.Deserialize(data).HandleAsync(this.Client);
    }
}
