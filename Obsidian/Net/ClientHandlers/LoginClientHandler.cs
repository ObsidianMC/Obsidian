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
        this.SendPacket(new SelectKnownPacksPacket
        {
            KnownPacks = [new() { Id = "core", Version = Server.ProtocolDescription, Namespace = "minecraft" }]
        });

        //This is very inconvenient
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Dialog.CodecKey, CodecRegistry.Dialog.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Dimensions.CodecKey, CodecRegistry.Dimensions.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Biomes.CodecKey, CodecRegistry.Biomes.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChatType.CodecKey, CodecRegistry.ChatType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.DamageType.CodecKey, CodecRegistry.DamageType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimPattern.CodecKey, CodecRegistry.TrimPattern.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimMaterial.CodecKey, CodecRegistry.TrimMaterial.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        this.SendPacket(new RegistryDataPacket(CodecRegistry.CatVariant.CodecKey, CodecRegistry.CatVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChickenVariant.CodecKey, CodecRegistry.ChickenVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.CowVariant.CodecKey, CodecRegistry.CowVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.FrogVariant.CodecKey, CodecRegistry.FrogVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.PigVariant.CodecKey, CodecRegistry.PigVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        //Figure out why sending all the wolf variants throw a network protocol error
        this.SendPacket(new RegistryDataPacket(CodecRegistry.WolfVariant.CodecKey, new Dictionary<string, ICodec>()
        {
            { CodecRegistry.WolfVariant.Black.Name, CodecRegistry.WolfVariant.Black },
        }));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.WolfSoundVariant.CodecKey, CodecRegistry.WolfSoundVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.PaintingVariant.CodecKey, CodecRegistry.PaintingVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        this.SendPacket(UpdateTagsPacket.ClientboundConfiguration with { Tags = TagsRegistry.Categories });

        this.SendPacket(FinishConfigurationPacket.Default);
    }

    private async Task HandleLoginStartAsync(byte[] data)
    {
        var loginStart = HelloPacket.Deserialize(data);

        var username = this.Server.Configuration.Network.MulitplayerDebugMode ? $"Player{Globals.Random.Next(1, 999)}" : loginStart.Username;
        var world = this.Server.DefaultWorld;

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
