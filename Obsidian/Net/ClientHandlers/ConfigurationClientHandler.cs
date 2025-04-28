using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets.Common;
using Obsidian.Net.Packets.Configuration.Clientbound;

namespace Obsidian.Net.ClientHandlers;
internal sealed class ConfigurationClientHandler : ClientHandler
{
    public async override ValueTask<bool> HandleAsync(PacketData packetData)
    {
        var (id, buffer) = packetData;

        switch (id)
        {
            case 0:
                {
                    if(await HandleFromPoolAsync<ClientInformationPacket>(buffer))
                    {
                        this.Configure();
                        return true;
                    }

                    return false;
                }
            case 2:
                return await HandleFromPoolAsync<CustomPayloadPacket>(buffer);
            case 3:
                return await HandleFromPoolAsync<Packets.Configuration.Serverbound.FinishConfigurationPacket>(buffer);
            case 4:
                return await HandleFromPoolAsync<KeepAlivePacket>(buffer);
            case 6:
                return await HandleFromPoolAsync<ResourcePackPacket>(buffer);
            default:
                this.Client.Logger.LogWarning("Configuration Packet({id}) {name} is not being handled.", id, PacketsRegistry.Configuration.ServerboundNames[id]);
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
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Biomes.CodecKey, CodecRegistry.Biomes.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.Dimensions.CodecKey, CodecRegistry.Dimensions.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.ChatType.CodecKey, CodecRegistry.ChatType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.DamageType.CodecKey, CodecRegistry.DamageType.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimPattern.CodecKey, CodecRegistry.TrimPattern.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.TrimMaterial.CodecKey, CodecRegistry.TrimMaterial.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.WolfVariant.CodecKey, new Dictionary<string, ICodec>()
        {
            { CodecRegistry.WolfVariant.Black.Name, CodecRegistry.WolfVariant.Black },
        }));
        this.SendPacket(new RegistryDataPacket(CodecRegistry.PaintingVariant.CodecKey, CodecRegistry.PaintingVariant.All.ToDictionary(x => x.Key, x => (ICodec)x.Value)));

        this.SendPacket(UpdateTagsPacket.ClientboundConfiguration with { Tags = TagsRegistry.Categories });

        this.SendPacket(FinishConfigurationPacket.Default);
    }
}
