//     Obsidian/RconServer.cs
//     Copyright (C) 2022

using Microsoft.Extensions.Logging;
using Obsidian.Commands.Framework;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using System.Buffers.Binary;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Obsidian.Net.Rcon;

public class RconServer
{
    private readonly ILogger logger;
    private readonly TcpListener listener;
    private readonly List<RconConnection> connections = new();

    private uint connectionId;

    private readonly DHParameters? dhParameters;
    private readonly AsymmetricCipherKeyPair? keyPair;

    private readonly InitData initData;

    public RconServer(ILogger logger, IConfig config, IServer server, CommandHandler commandHandler)
    {
        this.logger = logger;
        var password = config.RconPassword;
        var pskKey = config.RconKey;
        var enableDiffieHellman = config.AllowDiffieHellman;

        if (enableDiffieHellman)
        {
            logger.LogInformation("Generating keys...");

            const int keySize = 256;
            const int certainty = 5;
            var gen = new DHParametersGenerator();
            gen.Init(keySize, certainty, new SecureRandom());
            dhParameters = gen.GenerateParameters();

            var keyGen = GeneratorUtilities.GetKeyPairGenerator("DH");
            var kgp = new DHKeyGenerationParameters(new SecureRandom(), dhParameters);
            keyGen.Init(kgp);
            keyPair = keyGen.GenerateKeyPair();

            logger.LogInformation("Done");
        }

        initData = new InitData(
            server, commandHandler, password, pskKey,
            enableDiffieHellman, dhParameters, keyPair);

        listener = TcpListener.Create(config.RconPort);
    }

    public async Task RunAsync(CancellationToken token)
    {
        _ = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
                try
                {
                    connections.RemoveAll(c => !c.Connected);
                    await Task.Delay(5000, token);
                }
                catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
                {
                    return;
                }
        }, token);

        connectionId = 0;
        listener.Start();
        logger.LogInformation("Started RCON server");

        while (!token.IsCancellationRequested)
            try
            {
                var conn = await listener.AcceptTcpClientAsync(token);
                logger.LogInformation("Accepting RCON connection ID {ConnectionId} from {RemoteAddress}", ++connectionId, conn.Client.RemoteEndPoint as IPEndPoint);
                connections.Add(new RconConnection(connectionId, conn, logger, initData, token));
            }
            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
            {
                break;
            }

        logger.LogInformation("Stopping RCON server");
        listener.Stop();
    }

    public record InitData(
        IServer Server, CommandHandler CommandHandler, string Password, string? PskKey,
        bool EnableDiffieHellman, DHParameters? DhParameters, AsymmetricCipherKeyPair? KeyPair);
}
