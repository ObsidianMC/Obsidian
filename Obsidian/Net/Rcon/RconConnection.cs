//     Obsidian/RconConnection.cs
//     Copyright (C) 2022

using Microsoft.Extensions.Logging;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Exceptions;
using System.Net.Sockets;
using System.Text;
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
    private Encoding currentEncoding = Encoding.ASCII;

    public bool Upgraded
    {
        get => currentEncoding.Equals(Encoding.UTF8);
        set => currentEncoding = value ? Encoding.UTF8 : Encoding.ASCII;
    }

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
                var packet = await RconPacket.ReadAsync(networkStream, cancellationToken, currentEncoding);

                if (packet is null)
                {
                    logger.LogDebug("Connection {ConnectionId} disconnected", Id);
                    break;
                }

                if (packet.Type == RconPacketType.Upgrade)
                {
                    Upgraded = true;
                    await new RconPacket(currentEncoding) { Type = RconPacketType.Upgrade, RequestId = packet.RequestId }.WriteAsync(
                        networkStream, cancellationToken);
                    continue;
                }

                if (!Authenticated)
                {
                    if (packet.Type != RconPacketType.Login)
                    {
                        var response = new RconPacket(currentEncoding)
                        {
                            Type = RconPacketType.Login, PayloadText = "Not authenticated", RequestId = -1
                        };
                        await response.WriteAsync(networkStream, cancellationToken);
                    }
                    else
                    {
                        if (packet.PayloadText != RconServer.Password)
                        {
                            await new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.Login, PayloadText = "Wrong password", RequestId = -1
                            }.WriteAsync(networkStream, cancellationToken);
                            break;
                        }

                        var response = new RconPacket(currentEncoding) {Type = RconPacketType.Command, RequestId = packet.RequestId};
                        await response.WriteAsync(networkStream, cancellationToken);
                        Authenticated = true;
                    }
                }
                else
                {
                    if (packet.Type == RconPacketType.Command)
                    {
                        // logger.LogInformation("Connection {ConnectionId} executed: '{Command}'", Id, packet.PayloadText);
                        //
                        // var response = new RconPacket
                        // {
                        //     Type = RconPacketType.CommandResponse,
                        //     PayloadText = $"Echo: '{packet.PayloadText}'",
                        //     RequestId = packet.RequestId
                        // };
                        // await response.WriteAsync(networkStream, cancellationToken);

                        var sender = new RconCommandSender();
                        var context = new CommandContext(CommandHandler.DefaultPrefix + packet.PayloadText, sender,
                            null, RconServer.Server);

                        try
                        {
                            await RconServer.CommandsHandler.ProcessCommand(context);

                            var commandResult = sender.GetResponse();
                            var response = new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.CommandResponse,
                                PayloadText = commandResult,
                                RequestId = packet.RequestId
                            };
                            await response.WriteAsync(networkStream, cancellationToken);
                        }
                        catch (CommandNotFoundException)
                        {
                            var response = new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.CommandResponse,
                                PayloadText = "Command not found",
                                RequestId = packet.RequestId
                            };
                            await response.WriteAsync(networkStream, cancellationToken);
                        }
                        catch (DisallowedCommandIssuerException disallowedCommandIssuerException)
                        {
                            var response = new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.CommandResponse,
                                PayloadText = disallowedCommandIssuerException.Message,
                                RequestId = packet.RequestId
                            };
                            await response.WriteAsync(networkStream, cancellationToken);
                        }
                        catch (Exception e)
                        {
                            logger.LogWarning(e, "Error processing RCON command");
                            var response = new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.CommandResponse,
                                PayloadText = $"Error: '{e.Message}'",
                                RequestId = packet.RequestId
                            };
                            await response.WriteAsync(networkStream, cancellationToken);
                        }
                    }
                    else
                    {
                        await new RconPacket(currentEncoding)
                        {
                            Type = RconPacketType.CommandResponse,
                            PayloadText = $"Unknown request 0x{(int)packet.Type:x2}",
                            RequestId = packet.RequestId
                        }.WriteAsync(networkStream, cancellationToken);
                    }
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
