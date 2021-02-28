using Microsoft.Extensions.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Serverbound;
using Obsidian.Util.Collection;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Obsidian
{
    public class ClientHandler
    {
        private readonly ConcurrentDictionary<int, IPacket> packets = new ConcurrentDictionary<int, IPacket>();

        public void RegisterHandlers()
        {
            // ! == moved to HandlePlayPackets
            //Packets.TryAdd(0x00, new TeleportConfirm()); !
            //Packets.TryAdd(0x01, QueryBlockNBT);
            //Packets.TryAdd(0x02, SetDifficulty);
            //Packets.TryAdd(0x03, new IncomingChatMessage()); !
            //Packets.TryAdd(0x04, ClientStatus);
            //Packets.TryAdd(0x05, new ClientSettings()); !
            //Packets.TryAdd(0x06, TabComplete);
            //Packets.TryAdd(0x07, new WindowConfirmation()); !
            //Packets.TryAdd(0x08, new ClickWindowButton()); !
            //Packets.TryAdd(0x09, new ClickWindow()); !
            //Packets.TryAdd(0x0A, new CloseWindow()); !
            //Packets.TryAdd(0x0B, new PluginMessage()); !
            //Packets.TryAdd(0x0C, EditBook);
            //packets.TryAdd(0x0E, new InteractEntity());
            //Packets.TryAdd(0x0F, GenerateStructure);
            packets.TryAdd(0x10, new KeepAlive());
            //Packets.TryAdd(0x11, LockDifficulty);
            packets.TryAdd(0x12, new PlayerPosition());
            packets.TryAdd(0x13, new ServerPlayerPositionLook());
            packets.TryAdd(0x14, new PlayerRotation());
            //Packets.TryAdd(0x15, PlayerMovement);
            //Packets.TryAdd(0x16, VehicleMove);
            //Packets.TryAdd(0x17, SteerBoat);
            //Packets.TryAdd(0x18, new PickItem()); !
            //Packets.TryAdd(0x19, new CraftRecipeRequest()); !
            //Packets.TryAdd(0x1A, PlayerAbilities);
            //Packets.TryAdd(0x1B, new PlayerDigging()); !
            //Packets.TryAdd(0x1C, new EntityAction()); !
            //Packets.TryAdd(0x1D, SteerVehicle);
            //Packets.TryAdd(0x1E, new SetDisplayedRecipe()); !
            //Packets.TryAdd(0x1F, SetRecipeBookState);
            //Packets.TryAdd(0x20, new NameItem()); !
            //Packets.TryAdd(0x21, ResourcePackStatus);
            //Packets.TryAdd(0x22, AdvancementTab);
            //Packets.TryAdd(0x23, SelectTrade);
            //Packets.TryAdd(0x24, SetBeaconEffect);
            packets.TryAdd(0x25, new ServerHeldItemChange());
            //Packets.TryAdd(0x26, UpdateCommandBlock);
            //Packets.TryAdd(0x27, UpdateCommandBlockMinecart);
            //Packets.TryAdd(0x28, new CreativeInventoryAction()); !
            //Packets.TryAdd(0x29, UpdateJigsawBlock);
            //Packets.TryAdd(0x2A, UpdateStructureBlock);
            //Packets.TryAdd(0x2B, UpdateSign);
            packets.TryAdd(0x2C, new Animation());
            //Packets.TryAdd(0x2D, Spectate);
            //Packets.TryAdd(0x2E, new PlayerBlockPlacement()); !
            //Packets.TryAdd(0x2F, UseItem);
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

                case 0x05:
                    await HandleFromPoolAsync<ClientSettings>(data, client);
                    break;

                case 0x07:
                    await HandleFromPoolAsync<WindowConfirmation>(data, client);
                    break;

                case 0x08:
                    await HandleFromPoolAsync<ClickWindowButton>(data, client);
                    break;

                case 0x09:
                    await HandleFromPoolAsync<ClickWindow>(data, client);
                    break;

                case 0x0A:
                    await HandleFromPoolAsync<CloseWindow>(data, client);
                    break;

                case 0x0B:
                    await HandleFromPoolAsync<PluginMessage>(data, client);
                    break;

                case 0x0E:
                    await HandleFromPoolAsync<InteractEntity>(data, client);
                    break;

                case 0x18:
                    await HandleFromPoolAsync<PickItem>(data, client);
                    break;

                case 0x19:
                    await HandleFromPoolAsync<CraftRecipeRequest>(data, client);
                    break;

                case 0x1B:
                    await HandleFromPoolAsync<PlayerDigging>(data, client);
                    break;

                case 0x1C:
                    await HandleFromPoolAsync<EntityAction>(data, client);
                    break;

                case 0x1E:
                    await HandleFromPoolAsync<SetDisplayedRecipe>(data, client);
                    break;

                case 0x20:
                    await HandleFromPoolAsync<NameItem>(data, client);
                    break;

                case 0x28:
                    await HandleFromPoolAsync<CreativeInventoryAction>(data, client);
                    break;

                case 0x2E:
                    await HandleFromPoolAsync<PlayerBlockPlacement>(data, client);
                    break;

                default:
                    if (!packets.TryGetValue(id, out var packet))
                        return;

                    try
                    {
                        await packet.ReadAsync(new MinecraftStream(data));
                        await packet.HandleAsync(client.Server, client.Player);
                    }
                    catch (Exception e)
                    {
                        if (Globals.Config.VerboseLogging)
                            Globals.PacketLogger.LogError(e.Message + Environment.NewLine + e.StackTrace);
                    }
                    break;
            }
        }

        private static async Task HandleFromPoolAsync<T>(byte[] data, Client client) where T : IPacket, new()
        {
            var packet = ObjectPool<T>.Shared.Rent();
            using var stream = new MinecraftStream(data);
            try
            {
                await packet.ReadAsync(stream);
                await packet.HandleAsync(client.Server, client.Player);
            }
            catch (Exception e)
            {
                if (Globals.Config.VerboseLogging)
                    Globals.PacketLogger.LogError(e.Message + Environment.NewLine + e.StackTrace);
            }
            ObjectPool<T>.Shared.Return(packet);
        }
    }
}