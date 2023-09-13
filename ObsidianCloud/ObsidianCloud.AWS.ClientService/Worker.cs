using Microsoft.AspNetCore.Connections;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Server.Kestrel.Transport.Sockets;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using Obsidian.Net;
using Obsidian.Utilities;
using Obsidian.Net.Packets;
using Obsidian.Net.Packets.Status;
using System.Net.Sockets;

namespace ObsidianCloud.AWS.ClientService;

public class Worker : BackgroundService
{
    private readonly ILogger<Worker> _logger;
    private readonly IHostApplicationLifetime _applicationLifetime;
    private IConnectionListener? _tcpListener;
    private readonly ServerConfiguration _config;
    private MinecraftStream minecraftStream;
    private ConnectionContext connectionContext;

    public Worker(ILogger<Worker> logger, IHostApplicationLifetime lifetime)
    {
        _logger = logger;
        _applicationLifetime = lifetime;
        _config = new ServerConfiguration();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var factory = new SocketTransportFactory(Options.Create(new SocketTransportOptions()), NullLoggerFactory.Instance);
        _tcpListener = await factory.BindAsync(new IPEndPoint(IPAddress.Any, _config.Port));
        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
            try
            {
                var acceptedConnection = await _tcpListener.AcceptAsync(stoppingToken);
                if (acceptedConnection is null)
                {
                    // No longer accepting clients.
                    break;
                }

                connectionContext = acceptedConnection;
            }
            catch (OperationCanceledException)
            {
                // No longer accepting clients.
                break;
            }
            catch (Exception e)
            {
                _logger.LogError(e, "Listening for clients encountered an exception");
                break;
            }
            _logger.LogDebug("New connection from client with IP {ip}", connectionContext.RemoteEndPoint);

            string ip = ((IPEndPoint)connectionContext.RemoteEndPoint!).Address.ToString();

            DuplexPipeStream networkStream = new(connectionContext.Transport);
            minecraftStream = new(networkStream);
            ClientState state = ClientState.Status;

            while (!stoppingToken.IsCancellationRequested && connectionContext.IsConnected()) 
            {
                (var id, var data) = await GetNextPacketAsync();
                switch(state)
                {
                    case ClientState.Status:
                        if (id == 0x01)
                        {
                            var pong = PingPong.Deserialize(data);
                            SendPacket(pong);
                            Disconnect();
                            break;       
                        }
                        break;
                }
            }

            await Task.Delay(1000, stoppingToken);
        }
    }

    private async Task<(int id, byte[] data)> GetNextPacketAsync()
    {
        var length = await minecraftStream.ReadVarIntAsync();
        var receivedData = new byte[length];

        _ = await minecraftStream.ReadAsync(receivedData.AsMemory(0, length));

        var packetId = 0;
        var packetData = Array.Empty<byte>();

        await using (var packetStream = new MinecraftStream(receivedData))
        {
            try
            {
                packetId = await packetStream.ReadVarIntAsync();
                var arlen = 0;

                if (length - packetId.GetVarIntLength() > -1)
                    arlen = length - packetId.GetVarIntLength();

                packetData = new byte[arlen];
                _ = await packetStream.ReadAsync(packetData.AsMemory(0, packetData.Length));
            }
            catch
            {
                throw;
            }
        }

        return (packetId, packetData);
    }

    internal void SendPacket(IClientboundPacket packet)
    {
        try
        {
            packet.Serialize(minecraftStream);
        }
        catch (SocketException)
        {
            // Clients can disconnect at any point, causing exception to be raised
            if (!connectionContext.IsConnected())
            {
                Disconnect();
            }
        }
        catch (Exception e)
        {
            _logger.LogDebug(e, "Sending packet {PacketId} failed", packet.Id);
        }
    }

    internal void Disconnect()
    {
        this.Dispose();
    }
}
