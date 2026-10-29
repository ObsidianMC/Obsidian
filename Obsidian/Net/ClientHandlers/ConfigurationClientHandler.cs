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

        this.SendPacket(KnownPacksPacket());

        var dialogs = this.Server.CreateDialogConfiguration();
        foreach (var registry in RegistryPackets(dialogs.Codecs))
            this.SendPacket(registry);

        // After the registries: the loom's tags and dimension types' timeline tags are numbered by them.
        this.SendPacket(UpdateTagsPacket.ClientboundConfiguration with { Tags = dialogs.Tags });

        this.SendPacket(FinishConfigurationPacket.Default);
    }

    /// <summary>
    /// The packs the server shares with clients: vanilla's <c>minecraft:core</c>, whose version is the game version's
    /// id, like vanilla's <c>KnownPack.vanilla</c>.
    /// </summary>
    internal static SelectKnownPacksPacket KnownPacksPacket() => new()
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
    };

    /// <summary>
    /// The registry data for every registry vanilla synchronizes, in vanilla's order. Registries Obsidian models are
    /// sent from its own tables, whose order gives the ids the rest of the server uses; the others are vanilla's, from
    /// <see cref="VanillaSyncedRegistries"/>. Like vanilla, entries of the core pack are sent by id; plugins' dimension
    /// types, biomes, damage types and dialogs, and vanilla's that files override, go with their data, which vanilla
    /// clients accept like a data pack's.
    /// </summary>
    /// <param name="dialogs">The server's dialogs, vanilla's first (see <see cref="Server.CreateDialogConfiguration"/>).</param>
    internal static IEnumerable<RegistryDataPacket> RegistryPackets(IDictionary<string, ICodec> dialogs)
    {
        foreach (var registry in VanillaSyncedRegistries.All)
        {
            yield return registry.Name switch
            {
                CodecRegistry.Biomes.CodecKey => new(registry.Name, CodecRegistry.Biomes.Table.NetworkEntries),
                CodecRegistry.ChatType.CodecKey => new(registry.Name, ById(CodecRegistry.ChatType.All)),
                CodecRegistry.TrimPattern.CodecKey => new(registry.Name, ById(CodecRegistry.TrimPattern.All)),
                CodecRegistry.TrimMaterial.CodecKey => new(registry.Name, ById(CodecRegistry.TrimMaterial.All)),
                CodecRegistry.WolfVariant.CodecKey => new(registry.Name, Names(CodecRegistry.WolfVariant.All.Values)),
                CodecRegistry.WolfSoundVariant.CodecKey => new(registry.Name, Names(CodecRegistry.WolfSoundVariant.All.Values)),
                CodecRegistry.PigVariant.CodecKey => new(registry.Name, Names(CodecRegistry.PigVariant.All.Values)),
                CodecRegistry.FrogVariant.CodecKey => new(registry.Name, Names(CodecRegistry.FrogVariant.All.Values)),
                CodecRegistry.CatVariant.CodecKey => new(registry.Name, Names(CodecRegistry.CatVariant.All.Values)),
                CodecRegistry.CowVariant.CodecKey => new(registry.Name, Names(CodecRegistry.CowVariant.All.Values)),
                CodecRegistry.ChickenVariant.CodecKey => new(registry.Name, Names(CodecRegistry.ChickenVariant.All.Values)),
                CodecRegistry.ZombieNautilusVariant.CodecKey =>
                    new(registry.Name, Names(CodecRegistry.ZombieNautilusVariant.All.Values)),
                CodecRegistry.PaintingVariant.CodecKey => new(registry.Name, ById(CodecRegistry.PaintingVariant.All)),
                CodecRegistry.Dimensions.CodecKey => new(registry.Name, CodecRegistry.Dimensions.Table.NetworkEntries),
                CodecRegistry.DamageType.CodecKey => new(registry.Name, CodecRegistry.DamageType.Table.NetworkEntries),
                BannerPatternsRegistry.CodecKey => new(registry.Name, BannerPatternsRegistry.Identifiers),
                // Item components refer to these by network id, e.g. an item's enchantments.
                "minecraft:enchantment" => new(registry.Name, EnchantmentsRegistry.All.Select(enchantment => enchantment.Identifier)),
                "minecraft:jukebox_song" => new(registry.Name, JukeboxSongsRegistry.Identifiers),
                "minecraft:instrument" => new(registry.Name, InstrumentsRegistry.All.Select(instrument => instrument.Identifier)),
                CodecRegistry.Dialog.CodecKey => new(registry.Name, dialogs)
                {
                    WriteCodecs = true,
                    KnownEntries = CodecRegistry.Dialog.All.Keys.ToHashSet()
                },
                TimelinesRegistry.CodecKey => new(registry.Name, TimelinesRegistry.Identifiers),
                _ => new(registry.Name, registry.Entries)
            };
        }
    }

    // A variant registry's names, by id.
    private static IEnumerable<string> Names<TCodec>(IEnumerable<TCodec> variants) where TCodec : ICodec =>
        variants.OrderBy(variant => variant.Id).Select(variant => variant.Name);

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Warning,
            Message = "Disconnecting a client that finished configuration before the server did")]
        public static partial void FinishedEarly(ILogger logger);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Configuration packet {PacketId} is not handled")]
        public static partial void UnhandledPacket(ILogger logger, int packetId);
    }
}
