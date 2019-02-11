using Newtonsoft.Json;
using Obsidian.Concurrency;
using Obsidian.Entities;
using Obsidian.Logging;
using Obsidian.Packets;
using Obsidian.Packets.Handshaking;
using Obsidian.Packets.Status;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.Connection
{
    public class Client
    {
        public TcpClient Tcp { get; private set; }
        public CancellationTokenSource Cancellation { get; private set; }
        private ConcurrentHashSet<Packet> PacketQueue;
        private Logger Logger;
        private Config Config;

        //current state of client
        public PacketState state { get; private set; } = PacketState.Handshaking;

        public Client(TcpClient tcp, Logger logger, Config config)
        {
            this.Tcp = tcp;
            this.Cancellation = new CancellationTokenSource();
            this.Logger = logger;
            this.Config = config;
        }

        public async Task StartClientConnection()
        {
            var netstream = Tcp.GetStream();
            while (!Cancellation.IsCancellationRequested)
            {
                var packet = await GetNextPacketAsync(netstream);
                switch (state)
                {
                    case PacketState.Handshaking: //Intial state / beginning
                        switch (packet.PacketId)
                        {
                            case 0x00:
                                // Client tries to handshake
                                //HACK: State check has been removed, might be worth adding since that might be a possible security/exploit threat.
                                var handshake = await Handshake.FromArrayAsync(packet.PacketData);
                                state = handshake.NextState;
                                await Logger.LogMessageAsync($"Client with protocol version {handshake.Version} knows us as {handshake.ServerAddress}:{handshake.ServerPort} and has been switched to state '{state}' per request");
                                break;
                        }
                        break;

                    case PacketState.Login:
                        switch (packet.PacketId)
                        {
                            default: 
                                await this.DisconnectClientAsync(new Chat()
                                {
                                    Text = Config.JoinMessage,
                                    /*Extra = new List<Chat>()
                                    {
                                        new Chat()
                                        {
                                            Text="barbecue sauce",
                                            Bold=true
                                        }
                                    }*/
                                });
                                break;
                        }
                        break;

                    case PacketState.Play:

                        break;

                    case PacketState.Status: //server ping/list
                        Packet pack = null;
                        switch (packet.PacketId)
                        {
                            case 0x00:
                                // Client wants that hot server data, lets give it
                                await Logger.LogMessageAsync("Received empty packet in STATUS state. Sending json status data.");
                                var res = new RequestResponse(JsonConvert.SerializeObject(ServerStatus.DebugStatus));
                                pack = new Packet(0x00, await res.GetDataStream());
                                await pack.WriteToStreamAsync(netstream);
                                break;
                                
                            case 0x01:
                                var ping = await PingPong.FromArrayAsync(packet.PacketData); // afaik you can just resend the ping to the client
                                await Logger.LogMessageAsync($"Client sent us ping request with payload {ping.Payload}");
                                
                                pack = new Packet(0x01, await ping.ToArrayAsync());
                                await pack.WriteToStreamAsync(netstream);
                                this.DisconnectClient();
                                break;
                        }
                        break;
                }

                // will paste that in a txt
            }
            await Logger.LogMessageAsync($"Disconnected client");
            this.Tcp.Close();
        }

        private async Task<Packet> GetNextPacketAsync(Stream stream)
        {
            return await Packet.ReadFromStreamAsync(stream);
        }

        public void QueuePacket(Packet packet)
        {
            PacketQueue.Add(packet);
        }

        ///Kicks a client with a reason
        public async Task DisconnectClientAsync(Chat reason)
        {
            var disconnect = new Disconnect(reason);
            var packet = new Packet(0x00, await disconnect.ToArrayAsync());
            await packet.WriteToStreamAsync(Tcp.GetStream());
            DisconnectClient();
        }

        public void DisconnectClient()
        {
            Cancellation.Cancel();
        }
    }
}
