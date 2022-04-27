//     Obsidian/RconConnection.cs
//     Copyright (C) 2022

using Microsoft.Extensions.Logging;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Exceptions;
using Org.BouncyCastle.Bcpg;
using System.Buffers.Binary;
using System.IO;
using System.Net.Sockets;
using System.Security.Cryptography;
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

    public EncryptionState EncryptionState { get; private set; } = EncryptionState.Unencrypted;
    public EncryptionMode EncryptionMode { get; private set; } = (EncryptionMode)(-1);
    private int encryptionChallenge;

    private ICryptoTransform? encryptor;
    private ICryptoTransform? decryptor;

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
                var packet = await ReceiveAsync();

                if (packet is null)
                {
                    logger.LogDebug("Connection {ConnectionId} disconnected", Id);
                    break;
                }

                if (packet.Type == RconPacketType.Upgrade)
                {
                    Upgraded = true;
                    await SendAsync(new RconPacket(currentEncoding)
                        { Type = RconPacketType.Upgrade, RequestId = packet.RequestId });
                    continue;
                }

                if (packet.Type >= RconPacketType.EncryptedContent && !Upgraded)
                {
                    await SendAsync(new RconPacket(currentEncoding)
                    {
                        Type = RconPacketType.CommandResponse,
                        PayloadText = $"Unknown request 0x{(int)packet.Type:x2}",
                        RequestId = packet.RequestId
                    });
                    continue;
                }

                if (packet.Type == RconPacketType.EncryptStart && EncryptionState == EncryptionState.Unencrypted)
                {
                    var mode = (EncryptionMode)BinaryPrimitives.ReadInt32LittleEndian(packet.PayloadBytes);
                    if (mode < EncryptionMode.PreSharedKey || !Enum.IsDefined(mode))
                    {
                        await SendAsync(new RconPacket(currentEncoding)
                        {
                            Type = RconPacketType.EncryptStart, RequestId = packet.RequestId
                        });
                        continue;
                    }

                    switch (mode)
                    {
                        case EncryptionMode.PreSharedKey when !string.IsNullOrWhiteSpace(RconServer.PskKey):
                        {
                            EncryptionState = EncryptionState.Challenge;
                            EncryptionMode = mode;

                            encryptionChallenge = Random.Shared.Next(1000000);

                            var buffer = new byte[4];
                            BinaryPrimitives.WriteInt32LittleEndian(buffer, encryptionChallenge);

                            using var aes = Aes.Create();

                            var iv = new byte[16];
                            var key = Encoding.UTF8.GetBytes(RconServer.PskKey);

                            aes.Key = key;
                            aes.IV = iv;

                            encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                            decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                            await SendAsync(Encrypt(new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.EncryptTest, RequestId = packet.RequestId, PayloadBytes = buffer
                            }));
                            continue;
                        }
                        case EncryptionMode.DiffieHellmanExchange when RconServer.EnableDiffieHellman:
                            // TODO
                            break;
                        default:
                            await SendAsync(new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.EncryptStart,
                                RequestId = packet.RequestId,
                                PayloadText = "Not allowed"
                            });
                            continue;
                    }
                }

                if (packet.Type == RconPacketType.EncryptTest && EncryptionState == EncryptionState.Challenge)
                {
                    var receivedChallenge = BinaryPrimitives.ReadInt32LittleEndian(packet.PayloadBytes);

                    if (receivedChallenge == 2 * encryptionChallenge)
                    {
                        EncryptionState = EncryptionState.Encrypted;
                        await SendAsync(new RconPacket(currentEncoding)
                        {
                            RequestId = packet.RequestId, Type = RconPacketType.EncryptSuccess
                        });
                        continue;
                    }

                    logger.LogWarning("Encryption challenge mismatch");
                    break;

                }

                if (packet.Type == RconPacketType.EncryptedContent && EncryptionState is not EncryptionState.Encrypted)
                {
                    await new RconPacket(currentEncoding)
                    {
                        RequestId = packet.RequestId, Type = RconPacketType.EncryptStart
                    }.WriteAsync(networkStream, cancellationToken);
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

    private async Task SendAsync(RconPacket packet, bool encrypt = true)
    {
        if (EncryptionState is EncryptionState.Encrypted && encrypt) packet = Encrypt(packet);

        await packet.WriteAsync(networkStream, cancellationToken);
    }

    private async Task<RconPacket?> ReceiveAsync(bool decrypt = true)
    {
        var packet = await RconPacket.ReadAsync(networkStream, cancellationToken, currentEncoding);

        if (packet is null)
            return null;

        if (packet.Type == RconPacketType.EncryptedContent && decrypt) packet = Decrypt(packet);

        return packet;
    }

    private RconPacket Encrypt(RconPacket packet)
    {
        using var ms = new MemoryStream();
        using var cs = new CryptoStream(ms, encryptor ?? throw new InvalidOperationException(), CryptoStreamMode.Write);

        packet.Write(cs);
        cs.FlushFinalBlock();

        var encrypted = ms.ToArray();

        var result = new RconPacket(currentEncoding)
        { RequestId = packet.RequestId, Type = RconPacketType.EncryptedContent, PayloadBytes = encrypted };
        return result;
    }

    private RconPacket Decrypt(RconPacket packet)
    {
        using var ms = new MemoryStream();
        using (var cs = new CryptoStream(ms, decryptor ?? throw new InvalidOperationException(), CryptoStreamMode.Write))
            cs.Write(packet.PayloadBytes);

        using var ms2 = new MemoryStream(ms.ToArray());

        var result = RconPacket.Read(ms2, currentEncoding);
        return result;
    }
}

public enum EncryptionState
{
    Unencrypted,
    Encrypted,
    Started,
    Challenge
}

public enum EncryptionMode
{
    PreSharedKey,
    DiffieHellmanExchange
}
