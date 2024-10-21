using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Configuration.Clientbound;
using Obsidian.Net.Packets.Configuration;
using Obsidian.Net.Packets.Login;
using Obsidian.Registries;
using Obsidian.WorldData;

namespace Obsidian.Net.ClientHandlers;
internal sealed class LoginClientHandler : ClientHandler
{
    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, data) = packetData;

        switch (id)
        {
            case 0x00:
                {
                    if (await this.Server.ShouldThrottleAsync(this.Client))
                        return false;

                    try
                    {
                        await this.HandleLoginStartAsync(data);
                    }
                    catch { return false; }

                    return true;
                }
            case 0x01:
                {
                    try
                    {
                        await this.HandleEncryptionResponseAsync(data);
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

                    this.Configure();

                    return true;
                }
            default:
                this.Logger.LogError("Client in state Login tried to send an unimplemented packet. Forcing it to disconnect.");
                await this.Client.DisconnectAsync("Unknown Packet Id.");
                break;
        }

        return false;
    }

    private void Configure()
    {
        //This is very inconvenient
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Biomes.CodecKey, CodecRegistry.Biomes.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Dimensions.CodecKey, CodecRegistry.Dimensions.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChatType.CodecKey, CodecRegistry.ChatType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.DamageType.CodecKey, CodecRegistry.DamageType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimPattern.CodecKey, CodecRegistry.TrimPattern.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimMaterial.CodecKey, CodecRegistry.TrimMaterial.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.WolfVariant.CodecKey, CodecRegistry.WolfVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.PaintingVariant.CodecKey, CodecRegistry.PaintingVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        this.SendPacket(UpdateTagsPacket.FromRegistry);

        this.SendPacket(FinishConfigurationPacket.Default);
    }

    private async Task HandleLoginStartAsync(byte[] data)
    {
        var loginStart = LoginStart.Deserialize(data);
        var username = this.Server.Configuration.Network.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;
        var world = (World)this.Server.DefaultWorld;

        this.Logger.LogDebug("Received login request from user {Username}", username);
        await this.Server.DisconnectIfConnectedAsync(username);

        if (this.Server.Configuration.OnlineMode &&
            await this.Client.TrySetCachedProfileAsync(username))
        {
            this.Client.Initialize(world);

            return;
        }

        if (!this.Server.IsWhitedlisted(username))
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
        var encryptionResponse = EncryptionResponse.Deserialize(data);

        await this.Client.TryValidateEncryptionResponseAsync(encryptionResponse.SharedSecret, encryptionResponse.VerifyToken);
    }
}
