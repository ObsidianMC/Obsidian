using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Configuration.Clientbound;
using Obsidian.Registries;
using ServerboundFinishConfigurationPacket = Obsidian.Net.Packets.Configuration.Serverbound.FinishConfigurationPacket;

namespace Obsidian.Net.ClientHandlers;
internal sealed partial class ConfigurationClientHandler : ClientHandler
{
    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, buffer) = packetData;

        switch (id)
        {
            case 0:
                return await HandleFromPoolAsync<ClientInformationPacket>(buffer);
            case 2:
                {
                    var payload = new CustomPayloadPacket();
                    payload.Populate(buffer);

                    await this.Client.Channels.HandlePayloadAsync(payload.Channel, payload.PluginData);

                    // A NeoForge client applied the registries plugins added to; vanilla's come next.
                    if (this.Client.Channels.TakeReadyForConfiguration())
                        this.SendConfiguration();

                    return true;
                }
            case 3:
                {
                    // Finishing before the server did would skip the channel handshake and the registries.
                    if (!this.configurationSent)
                    {
                        Log.FinishedEarly(this.Logger);
                        await this.Client.DisconnectAsync("Finished configuration before the server.");
                        return false;
                    }

                    if (!await HandleFromPoolAsync<ServerboundFinishConfigurationPacket>(buffer))
                        return false;

                    await this.Client.Channels.FinishConfigurationAsync();
                    return true;
                }
            case 4:
                return await HandleFromPoolAsync<KeepAlivePacket>(buffer);
            case 5:
                {
                    var pong = new PongPacket();
                    pong.Populate(buffer);

                    if (await this.Client.Channels.FinishHandshakeAsync(pong.IdValue))
                        this.SendConfiguration();

                    return true;
                }
            case 6:
                return await HandleFromPoolAsync<ResourcePackPacket>(buffer);
            default:
                Log.UnhandledPacket(this.Logger, id);
                break;
        }

        return false;
    }

    // Whether the server sent its configuration and FinishConfiguration, after which the client may finish.
    private bool configurationSent;

    /// <summary>
    /// Sends the server's known packs, registries and tags, then finishes configuration.
    /// </summary>
    private void SendConfiguration()
    {
        this.configurationSent = true;

        this.SendPacket(new SelectKnownPacksPacket
        {
            KnownPacks =
            [
                new()
                {
                    Id = "core",
                    Version = ServerConstants.ProtocolDescription,
                    Namespace = "minecraft"
                }
            ]
        });

        //This is very inconvenient
        var dialogs = this.Server.CreateDialogConfiguration();
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Dialog.CodecKey, dialogs.Codecs) { WriteCodecs = true });
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Dimensions.CodecKey,
            CodecRegistry.Dimensions.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Biomes.CodecKey,
            CodecRegistry.Biomes.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChatType.CodecKey,
            CodecRegistry.ChatType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.DamageType.CodecKey,
            CodecRegistry.DamageType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimPattern.CodecKey,
            CodecRegistry.TrimPattern.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimMaterial.CodecKey,
            CodecRegistry.TrimMaterial.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        this.SendPacket(new RegistryDataPacket(CodecRegistry.CatVariant.CodecKey,
            CodecRegistry.CatVariant.All.Values.OrderBy(variant => variant.Id).Select(variant => variant.Name)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChickenVariant.CodecKey,
            CodecRegistry.ChickenVariant.All.Values.OrderBy(variant => variant.Id).Select(variant => variant.Name)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.CowVariant.CodecKey,
            CodecRegistry.CowVariant.All.Values.OrderBy(variant => variant.Id).Select(variant => variant.Name)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.FrogVariant.CodecKey,
            CodecRegistry.FrogVariant.All.Values.OrderBy(variant => variant.Id).Select(variant => variant.Name)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.PigVariant.CodecKey,
            CodecRegistry.PigVariant.All.Values.OrderBy(variant => variant.Id).Select(variant => variant.Name)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ZombieNautilusVariant.CodecKey,
            CodecRegistry.ZombieNautilusVariant.All.Values
                .OrderBy(variant => variant.Id)
                .Select(variant => variant.Name)));

        this.SendPacket(new RegistryDataPacket(CodecRegistry.WolfVariant.CodecKey,
            CodecRegistry.WolfVariant.All.Values.OrderBy(variant => variant.Id).Select(variant => variant.Name)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.WolfSoundVariant.CodecKey,
            CodecRegistry.WolfSoundVariant.All.Values.OrderBy(variant => variant.Id).Select(variant => variant.Name)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.PaintingVariant.CodecKey,
            CodecRegistry.PaintingVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        // Item components refer to these by network id (the index of the entry sent here), e.g. an item's
        // enchantments.
        var enchantments = EnchantmentsRegistry.All.Select(enchantment => enchantment.Identifier);
        var instruments = InstrumentsRegistry.All.Select(instrument => instrument.Identifier);

        this.SendPacket(new RegistryDataPacket("minecraft:enchantment", enchantments));
        this.SendPacket(new RegistryDataPacket("minecraft:instrument", instruments));
        this.SendPacket(new RegistryDataPacket("minecraft:jukebox_song", JukeboxSongsRegistry.Identifiers));

        this.SendPacket(UpdateTagsPacket.ClientboundConfiguration with { Tags = dialogs.Tags });

        this.SendPacket(FinishConfigurationPacket.Default);
    }

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Disconnecting a client that finished configuration before the server did")]
        public static partial void FinishedEarly(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Configuration packet {PacketId} is not handled")]
        public static partial void UnhandledPacket(ILogger logger, int packetId);
    }
}
