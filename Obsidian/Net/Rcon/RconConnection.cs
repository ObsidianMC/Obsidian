//     Obsidian/RconConnection.cs
//     Copyright (C) 2022

using Microsoft.CodeAnalysis.FlowAnalysis;
using Microsoft.Extensions.Logging;
using System.Net.Sockets;
using System.Threading;

namespace Obsidian.Net.Rcon;

public class RconConnection
{
    public uint Id { get; }
    public TcpClient Client { get; }

    public bool Connected { get; private set; } = true;
    public bool Authenticated { get; private set; }
    
    private readonly CancellationToken cancellationToken;
    private readonly ILogger logger;
    private readonly NetworkStream networkStream;

    private bool stop;

    public RconConnection(uint connectionId, TcpClient conn, ILogger logger, CancellationToken cancellationToken)
    {
        Id = connectionId;
        Client = conn;
        this.logger = logger;
        this.cancellationToken = cancellationToken;
        networkStream = conn.GetStream();

        _ = Task.Run(ReceiveLoop, cancellationToken);
    }

    private async Task ReceiveLoop()
    {
        while (!cancellationToken.IsCancellationRequested)
            try
            {
                var packet = await RconPacket.ReadAsync(networkStream, cancellationToken);

                if (packet is null)
                {
                    logger.LogInformation("Connection {ConnectionId} disconnected", Id);
                    break;
                }
                
                if (!Authenticated)
                {
                    if (packet.Type != RconPacketType.Login)
                    {
                        var response = new RconPacket
                        {
                            Type = RconPacketType.Login, PayloadText = "Not authenticated", RequestId = -1
                        };
                        await response.WriteAsync(networkStream, cancellationToken);
                    }
                    else
                    {
                        if (packet.PayloadText != RconServer.Password) continue;
                        
                        var response = new RconPacket {Type = RconPacketType.Command, RequestId = packet.RequestId};
                        await response.WriteAsync(networkStream, cancellationToken);
                        Authenticated = true;
                    }
                }
                else
                {
                    logger.LogInformation("Connection {ConnectionId} executed: '{Command}'", Id, packet.PayloadText);
                    
                    var response = new RconPacket
                    {
                        Type = RconPacketType.CommandResponse,
                        PayloadText = $"Echo: '{packet.PayloadText}'",
                        RequestId = packet.RequestId
                    };
                    await response.WriteAsync(networkStream, cancellationToken);
                }
            }
            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
            {
                logger.LogWarning(e, "Connection {ConnectionId} disconnected forcibly", Id);
                break;
            }

        Connected = false;
        Client.Close();
    }
}
