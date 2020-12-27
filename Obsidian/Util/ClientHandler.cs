using Microsoft.Extensions.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Serverbound;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Obsidian
{
    public class ClientHandler
    {
        private ConcurrentDictionary<int, IPacket> packets { get; } = new ConcurrentDictionary<int, IPacket>();

        public void RegisterHandlers()
        {
            packets.TryAdd(0x00, new TeleportConfirm());
            //Packets.TryAdd(0x01, QueryBlockNBT);
            //Packets.TryAdd(0x02, SetDifficulty);
            packets.TryAdd(0x03, new IncomingChatMessage());
            //Packets.TryAdd(0x04, ClientStatus);
            packets.TryAdd(0x05, new ClientSettings());
            //Packets.TryAdd(0x06, TabComplete);
            packets.TryAdd(0x07, new WindowConfirmation());
            packets.TryAdd(0x08, new ClickWindowButton());
            packets.TryAdd(0x09, new ClickWindow());
            packets.TryAdd(0x0A, new CloseWindow());
            packets.TryAdd(0x0B, new PluginMessage());
            //Packets.TryAdd(0x0C, EditBook);
            //Packets.TryAdd(0x0E, InteractEntity);
            //Packets.TryAdd(0x0F, GenerateStructure);
            packets.TryAdd(0x10, new KeepAlive());
            //Packets.TryAdd(0x11, LockDifficulty);
            packets.TryAdd(0x12, new PlayerPosition());
            packets.TryAdd(0x13, new ServerPlayerPositionLook());
            packets.TryAdd(0x14, new PlayerRotation());
            //Packets.TryAdd(0x15, PlayerMovement);
            //Packets.TryAdd(0x16, VehicleMove);
            //Packets.TryAdd(0x17, SteerBoat);
            packets.TryAdd(0x18, new PickItem());
            packets.TryAdd(0x19, new CraftRecipeRequest());
            //Packets.TryAdd(0x1A, PlayerAbilities);
            packets.TryAdd(0x1B, new PlayerDigging());
            packets.TryAdd(0x1C, new EntityAction());
            //Packets.TryAdd(0x1D, SteerVehicle);
            packets.TryAdd(0x1E, new SetDisplayedRecipe());
            //Packets.TryAdd(0x1F, SetRecipeBookState);
            packets.TryAdd(0x20, new NameItem());
            //Packets.TryAdd(0x21, ResourcePackStatus);
            //Packets.TryAdd(0x22, AdvancementTab);
            //Packets.TryAdd(0x23, SelectTrade);
            //Packets.TryAdd(0x24, SetBeaconEffect);
            packets.TryAdd(0x25, new ServerHeldItemChange());
            //Packets.TryAdd(0x26, UpdateCommandBlock);
            //Packets.TryAdd(0x27, UpdateCommandBlockMinecart);
            packets.TryAdd(0x28, new CreativeInventoryAction());
            //Packets.TryAdd(0x29, UpdateJigsawBlock);
            //Packets.TryAdd(0x2A, UpdateStructureBlock);
            //Packets.TryAdd(0x2B, UpdateSign);
            packets.TryAdd(0x2C, new Animation());
            //Packets.TryAdd(0x2D, Spectate);
            packets.TryAdd(0x2E, new PlayerBlockPlacement());
            //Packets.TryAdd(0x2F, UseItem);
        }

        public async Task HandlePlayPackets((int id, byte[] data) packet, Client client)
        {
            if (!packets.ContainsKey(packet.id))
                return;

            try
            {
                await packets[packet.id].ReadAsync(new MinecraftStream(packet.data));
                await packets[packet.id].HandleAsync(client.Server, client.Player);
            }
            catch(Exception e)
            {
                if (Globals.Config.VerboseLogging)
                    Globals.PacketLogger.LogError(e.Message + "\n" + e.StackTrace);
            }

        }
    }
}