﻿using Microsoft.Extensions.Logging;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Clientbound;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Utilities.Collection;

namespace Obsidian;

public class ClientHandler
{
    private ConcurrentDictionary<int, IServerboundPacket> Packets { get; } = new ConcurrentDictionary<int, IServerboundPacket>();
    private Config config;

    public ClientHandler(Config config)
    {
        this.config = config;
    }

    public void RegisterHandlers()
    {
        Packets.TryAdd(0x11, new PlayerPosition());
        Packets.TryAdd(0x12, new PlayerPositionAndRotation());
        Packets.TryAdd(0x13, new PlayerRotation());
        Packets.TryAdd(0x25, new ServerHeldItemChange());
        Packets.TryAdd(0x2F, new UseItem());
    }

    public async Task HandlePlayPackets(int id, byte[] data, Client client)
    {
        switch (id)
        {
            case 0x00:
                await HandleFromPoolAsync<TeleportConfirm>(data, client);
                break;

            case 0x03:
                await HandleFromPoolAsync<IncomingChatMessage>(data, client);
                break;
            case 0x04:
                await HandleFromPoolAsync<ClientStatus>(data, client);
                break;
            case 0x05:
                await HandleFromPoolAsync<ClientSettings>(data, client);
                break;

            case 0x07:
                await HandleFromPoolAsync<ClickWindowButton>(data, client);
                break;

            case 0x08:
                await HandleFromPoolAsync<ClickWindowPacket>(data, client);
                break;

            case 0x09:
                await HandleFromPoolAsync<CloseWindow>(data, client);
                break;

            case 0x0A:
                await HandleFromPoolAsync<ServerboundPluginMessage>(data, client);
                break;
            case 0x0F:
                await HandleFromPoolAsync<KeepAlivePacket>(data, client);
                break;

            case 0x0D:
                await HandleFromPoolAsync<InteractEntity>(data, client);
                break;

            case 0x17:
                await HandleFromPoolAsync<PickItem>(data, client);
                break;

            case 0x18:
                await HandleFromPoolAsync<CraftRecipeRequest>(data, client);
                break;

            case 0x1A:
                await HandleFromPoolAsync<PlayerDigging>(data, client);
                break;

            case 0x1B:
                await HandleFromPoolAsync<EntityAction>(data, client);
                break;

            case 0x1F:
                await HandleFromPoolAsync<SetDisplayedRecipe>(data, client);
                break;

            case 0x20:
                await HandleFromPoolAsync<NameItem>(data, client);
                break;

            case 0x28:
                await HandleFromPoolAsync<CreativeInventoryAction>(data, client);
                break;

            case 0x2C:
                await HandleFromPoolAsync<AnimationPacket>(data, client);
                break;

            case 0x2E:
                await HandleFromPoolAsync<PlayerBlockPlacement>(data, client);
                break;

            default:
                if (!Packets.TryGetValue(id, out var packet))
                    return;

                try
                {
                    packet.Populate(data);
                    await packet.HandleAsync(client.Server, client.Player);
                }
                catch (Exception e)
                {
                    if (this.config.VerboseExceptionLogging)
                        Globals.PacketLogger.LogError(e.Message + Environment.NewLine + e.StackTrace);
                }
                break;
        }
    }

    private static async Task HandleFromPoolAsync<T>(byte[] data, Client client) where T : IServerboundPacket, new()
    {
        var packet = ObjectPool<T>.Shared.Rent();
        try
        {
            packet.Populate(data);
            await packet.HandleAsync(client.Server, client.Player);
        }
        catch (Exception e)
        {
            if (client.Server.Config.VerboseExceptionLogging)
                Globals.PacketLogger.LogError(e.Message + Environment.NewLine + e.StackTrace);
        }
        ObjectPool<T>.Shared.Return(packet);
    }
}
