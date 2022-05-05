//     Obsidian/RconConnection.cs
//     Copyright (C) 2022

using Microsoft.Extensions.Logging;
using Obsidian.Commands.Framework;
using Obsidian.Commands.Framework.Exceptions;
using Org.BouncyCastle.Asn1.Nist;
using Org.BouncyCastle.Bcpg;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
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

    private readonly IServer server;
    private readonly CommandHandler commandHandler;
    private readonly string password;
    private byte[] pskKey;
    private readonly bool enableDiffieHellman;
    private readonly DHParameters? dhParameters;
    private readonly AsymmetricCipherKeyPair? keyPair;

    public RconConnection(uint connectionId, TcpClient conn, ILogger logger, RconServer.InitData initData, CancellationToken cancellationToken)
    {
        Id = connectionId;
        Client = conn;
        this.logger = logger;
        this.cancellationToken = cancellationToken;
        networkStream = conn.GetStream();

        server = initData.Server;
        commandHandler = initData.CommandHandler;
        password = initData.Password;
        pskKey = initData.PskKey is null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(initData.PskKey);
        enableDiffieHellman = initData.EnableDiffieHellman;
        dhParameters = initData.DhParameters;
        keyPair = initData.KeyPair;

        _ = Task.Run(ReceiveLoop, cancellationToken);
    }

    private static AsymmetricCipherKeyPair GenerateKeyPair(ECDomainParameters ecDomain)
    {
        var gen = (ECKeyPairGenerator)GeneratorUtilities.GetKeyPairGenerator("ECDH");
        gen.Init(new ECKeyGenerationParameters(ecDomain, new SecureRandom()));

        var keyPair = gen.GenerateKeyPair();
        return keyPair;
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

                    switch (mode)
                    {
                        case EncryptionMode.PreSharedKey when pskKey.Length != 0:
                        {
                            EncryptionState = EncryptionState.Challenge;
                            EncryptionMode = mode;

                            encryptionChallenge = Random.Shared.Next(1000000);

                            var buffer = new byte[4];
                            BinaryPrimitives.WriteInt32LittleEndian(buffer, encryptionChallenge);

                            using var aes = Aes.Create();

                            var iv = new byte[16];

                            aes.Key = pskKey;
                            aes.IV = iv;

                            encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                            decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                            await SendAsync(Encrypt(new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.EncryptTest, RequestId = packet.RequestId, PayloadBytes = buffer
                            }));
                            continue;
                        }
                        case EncryptionMode.DiffieHellmanExchange when enableDiffieHellman:
                        {
                            ArgumentNullException.ThrowIfNull(dhParameters);
                            ArgumentNullException.ThrowIfNull(keyPair);

                            EncryptionState = EncryptionState.Started;
                            EncryptionMode = mode;

                            var bytes = new List<byte>();
                            var buffer = new byte[4];

                            var tempBytes = Encoding.UTF8.GetBytes(dhParameters.P.ToString());
                            BinaryPrimitives.WriteInt32LittleEndian(buffer, tempBytes.Length);
                            bytes.AddRange(buffer);
                            bytes.AddRange(tempBytes);

                            tempBytes = Encoding.UTF8.GetBytes(dhParameters.G.ToString());
                            BinaryPrimitives.WriteInt32LittleEndian(buffer, tempBytes.Length);
                            bytes.AddRange(buffer);
                            bytes.AddRange(tempBytes);

                            tempBytes = Encoding.UTF8.GetBytes((keyPair.Public as DHPublicKeyParameters)!.Y.ToString());
                            BinaryPrimitives.WriteInt32LittleEndian(buffer, tempBytes.Length);
                            bytes.AddRange(buffer);
                            bytes.AddRange(tempBytes);

                            await SendAsync(new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.EncryptInitial,
                                RequestId = packet.RequestId,
                                PayloadBytes = bytes.ToArray()
                            });
                            continue;
                        }
                        default:
                            await SendAsync(new RconPacket(currentEncoding)
                            {
                                Type = RconPacketType.EncryptStart,
                                RequestId = packet.RequestId,
                            });
                            continue;
                    }
                }

                if (EncryptionState == EncryptionState.Started && packet.Type == RconPacketType.EncryptClientPublic)
                {
                    var length = BinaryPrimitives.ReadInt32LittleEndian(packet.PayloadBytes);
                    var segment = new ArraySegment<byte>(packet.PayloadBytes, 4, length);
                    var clientPublicKey = Encoding.UTF8.GetString(segment);

                    var sharedKey = ComputeSharedKey(clientPublicKey);

                    pskKey = sharedKey.ToByteArray();

                    EncryptionState = EncryptionState.Challenge;

                    encryptionChallenge = Random.Shared.Next(1000000);

                    var buffer = new byte[4];
                    BinaryPrimitives.WriteInt32LittleEndian(buffer, encryptionChallenge);

                    using var aes = Aes.Create();

                    var iv = new byte[16];

                    aes.Key = pskKey;
                    aes.IV = iv;

                    encryptor = aes.CreateEncryptor(aes.Key, aes.IV);
                    decryptor = aes.CreateDecryptor(aes.Key, aes.IV);

                    await SendAsync(Encrypt(new RconPacket(currentEncoding)
                    {
                        Type = RconPacketType.EncryptTest, RequestId = packet.RequestId, PayloadBytes = buffer
                    }));
                    continue;
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
                        if (!packet.PayloadText.Equals(password))
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
                            null, server);

                        try
                        {
                            await commandHandler.ProcessCommand(context);

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
                        await new RconPacket(currentEncoding)
                        {
                            Type = RconPacketType.CommandResponse,
                            PayloadText = $"Unknown request 0x{(int)packet.Type:x2}",
                            RequestId = packet.RequestId
                        }.WriteAsync(networkStream, cancellationToken);
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

    private BigInteger ComputeSharedKey(string clientKeyString)
    {
        ArgumentNullException.ThrowIfNull(keyPair);

        var clientKey = new DHPublicKeyParameters(new BigInteger(clientKeyString), dhParameters);
        var internalKeyAgree = AgreementUtilities.GetBasicAgreement("DH");
        internalKeyAgree.Init(keyPair.Private);

        return internalKeyAgree.CalculateAgreement(clientKey);
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
