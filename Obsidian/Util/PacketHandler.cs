using Microsoft.Extensions.Logging;
using Obsidian.Net;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Play;
using Obsidian.Net.Packets.Play.Server;
using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;

namespace Obsidian
{
    public class PacketHandler
    {
        public static ILogger Logger => Globals.PacketLogger;

        public static ConcurrentDictionary<int, IPacket> Packets { get; } = new ConcurrentDictionary<int, IPacket>();


        /*public static async Task<IPacket> ReadCompressedPacketAsync(MinecraftStream stream)//TODO
        {
            var packetLength = await stream.ReadVarIntAsync();
            var dataLength = await stream.ReadVarIntAsync();

            using var deStream = new MinecraftStream(new ZlibStream(stream, SharpCompress.Compressors.CompressionMode.Decompress, CompressionLevel.BestSpeed));

            var packetId = await deStream.ReadVarIntAsync();
            var packetData = await deStream.ReadUInt8ArrayAsync(dataLength - packetId.GetVarIntLength());

            return new Packet(packetId, packetData);
        }*/



        public static void RegisterHandlers()
        {
            Packets.TryAdd(0x00, new TeleportConfirm());
            //Packets.TryAdd(0x01, QueryBlockNBT);
            //Packets.TryAdd(0x02, SetDifficulty);
            Packets.TryAdd(0x03, new IncomingChatMessage());
            //Packets.TryAdd(0x04, ClientStatus);
            Packets.TryAdd(0x05, new ClientSettings());
            //Packets.TryAdd(0x06, TabComplete);
            Packets.TryAdd(0x07, new WindowConfirmation());
            Packets.TryAdd(0x08, new ClickWindowButton());
            Packets.TryAdd(0x09, new ClickWindow());
            Packets.TryAdd(0x0A, new CloseWindow());
            Packets.TryAdd(0x0B, new PluginMessage());
            //Packets.TryAdd(0x0C, EditBook);
            //Packets.TryAdd(0x0E, InteractEntity);
            //Packets.TryAdd(0x0F, GenerateStructure);
            Packets.TryAdd(0x10, new KeepAlive());
            //Packets.TryAdd(0x11, LockDifficulty);
            Packets.TryAdd(0x12, new PlayerPosition());
            Packets.TryAdd(0x13, new ServerPlayerPositionLook());
            Packets.TryAdd(0x14, new PlayerRotation());
            //Packets.TryAdd(0x15, PlayerMovement);
            //Packets.TryAdd(0x16, VehicleMove);
            //Packets.TryAdd(0x17, SteerBoat);
            Packets.TryAdd(0x18, new PickItem());
            //Packets.TryAdd(0x19, CraftRecipeRequest);
            //Packets.TryAdd(0x1A, PlayerAbilities);
            Packets.TryAdd(0x1B, new PlayerDigging());
            Packets.TryAdd(0x1C, new EntityAction());
            //Packets.TryAdd(0x1D, SteerVehicle);
            //Packets.TryAdd(0x1E, SetDisplayRecipe);
            //Packets.TryAdd(0x1F, SetRecipeBookSState);
            Packets.TryAdd(0x20, new NameItem());
            //Packets.TryAdd(0x21, ResourcePackStatus);
            //Packets.TryAdd(0x22, AdvancementTab);
            //Packets.TryAdd(0x23, SelectTrade);
            //Packets.TryAdd(0x24, SetBeaconEffect);
            Packets.TryAdd(0x25, new ServerHeldItemChange());
            //Packets.TryAdd(0x26, UpdateCommandBlock);
            //Packets.TryAdd(0x27, UpdateCommandBlockMinecart);
            Packets.TryAdd(0x28, new CreativeInventoryAction());
            //Packets.TryAdd(0x29, UpdateJigsawBlock);
            //Packets.TryAdd(0x2A, UpdateStructureBlock);
            //Packets.TryAdd(0x2B, UpdateSign);
            Packets.TryAdd(0x2C, new Animation());
            //Packets.TryAdd(0x2D, Spectate);
            Packets.TryAdd(0x2E, new PlayerBlockPlacement());
            //Packets.TryAdd(0x2F, UseItem);
        }

        public static async Task HandlePlayPackets((int id, byte[] data) packet, Client client)
        {
            if (!Packets.ContainsKey(packet.id))
                return;

            try
            {
                await Packets[packet.id].ReadAsync(new MinecraftStream(packet.data));
                await Packets[packet.id].HandleAsync(client.Server, client.Player);
            }
            catch(Exception e)
            {
                Logger.LogError(e.Message + "\n" + e.StackTrace);
            }

        }

    }
}