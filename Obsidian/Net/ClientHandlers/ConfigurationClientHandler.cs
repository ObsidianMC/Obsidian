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
            case ClientInformationPacket.ServerboundConfigurationId:
                return await HandleFromPoolAsync<ClientInformationPacket>(buffer);
            case CustomPayloadPacket.ServerboundConfigurationId:
                {
                    var payload = new CustomPayloadPacket();
                    payload.Populate(buffer);

                    await this.Client.Channels.HandlePayloadAsync(payload.Channel, payload.PluginData);

                    // A NeoForge client applied the registries plugins added to; vanilla's come next.
                    if (this.Client.Channels.TakeReadyForConfiguration())
                        this.SendConfiguration();

                    return true;
                }
            case ServerboundFinishConfigurationPacket.PacketId:
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

                    // Plugins changed the content while the client was configured with the earlier one.
                    var server = (Server)this.Server;
                    var current = server.GetContentVersion();
                    if (this.Client.NeedsReconfiguration(current))
                    {
                        this.Client.RequestReconfiguration();
                    }
                    else if (this.Client.State == ClientState.Play && this.Client.ConfiguredContent != current)
                    {
                        var tags = server.CreateDialogConfiguration().Tags;
                        await this.Client.QueuePacketAsync(UpdateTagsPacket.ClientboundPlay with { Tags = tags });
                    }

                    return true;
                }
            case KeepAlivePacket.ServerboundConfigurationId:
                return await HandleFromPoolAsync<KeepAlivePacket>(buffer);
            case PongPacket.ServerboundConfigurationId:
                {
                    var pong = new PongPacket();
                    pong.Populate(buffer);

                    if (await this.Client.Channels.FinishHandshakeAsync(pong.IdValue))
                        this.SendConfiguration();

                    return true;
                }
            case ResourcePackPacket.ServerboundConfigurationId:
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
    /// Starts over for a client going through configuration again.
    /// </summary>
    internal void Reset() => this.configurationSent = false;

    // A registry's entries in id order: the order they're sent in gives clients their ids.
    private static Dictionary<string, ICodec> ById<TCodec>(IEnumerable<KeyValuePair<string, TCodec>> entries)
        where TCodec : ICodec =>
        entries.OrderBy(entry => entry.Value.Id).ToDictionary(entry => entry.Key, entry => (ICodec)entry.Value);

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
        // Plugins' dimension types, biomes and damage types, and vanilla's that files override, are sent with their
        // data, which vanilla clients accept like a data pack's; vanilla's others by name from the core pack.
        this.SendPacket(new RegistryDataPacket(
            CodecRegistry.Dimensions.CodecKey,
            CodecRegistry.Dimensions.Table.NetworkEntries));
        this.SendPacket(new RegistryDataPacket(
            CodecRegistry.Biomes.CodecKey,
            CodecRegistry.Biomes.Table.NetworkEntries));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChatType.CodecKey, ById(CodecRegistry.ChatType.All)));
        this.SendPacket(new RegistryDataPacket(
            CodecRegistry.DamageType.CodecKey,
            CodecRegistry.DamageType.Table.NetworkEntries));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimPattern.CodecKey,
            ById(CodecRegistry.TrimPattern.All)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimMaterial.CodecKey,
            ById(CodecRegistry.TrimMaterial.All)));

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
            ById(CodecRegistry.PaintingVariant.All)));

        // Item components refer to these by network id (the index of the entry sent here), e.g. an item's
        // enchantments.
        var enchantments = EnchantmentsRegistry.All.Select(enchantment => enchantment.Identifier);
        var instruments = InstrumentsRegistry.All.Select(instrument => instrument.Identifier);

        this.SendPacket(new RegistryDataPacket("minecraft:enchantment", enchantments));
        this.SendPacket(new RegistryDataPacket("minecraft:instrument", instruments));
        this.SendPacket(new RegistryDataPacket("minecraft:jukebox_song", JukeboxSongsRegistry.Identifiers));
        // Before the tags: the loom's tags and dimension types' timeline tags are numbered by these registries.
        this.SendPacket(new RegistryDataPacket(BannerPatternsRegistry.CodecKey, BannerPatternsRegistry.Identifiers));
        this.SendPacket(new RegistryDataPacket(TimelinesRegistry.CodecKey, TimelinesRegistry.Identifiers));

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
