namespace Obsidian.Net.Rcon;
//public sealed class RconServer(ILogger<RconServer> logger, IOptions<ServerConfiguration> config, CommandHandler commandHandler)
//{
//    private const int KEY_SIZE = 256;
//    private const int CERTAINTY = 5;

//    private readonly ILogger _logger = logger;
//    private readonly IOptions<ServerConfiguration> _config = config;
//    private readonly CommandHandler _cmdHandler = commandHandler;
//    private readonly List<RconConnection> _connections = new();

//    public async Task RunAsync(Server server, CancellationToken cToken)
//    {
//        _logger.LogInformation(message: "Generating keys for RCON");
//        var data = GenerateKeys(server);
//        _logger.LogInformation("Done generating keys for RCON");

//        var tcpListener = TcpListener.Create(_config.Value.Rcon?.Port ?? 25575);

//        _ = Task.Run(async () =>
//        {
//            while (!cToken.IsCancellationRequested)
//                try
//                {
//                    _connections.RemoveAll(c => !c.Connected);
//                    await Task.Delay(5000, cToken);
//                }
//                catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
//                {
//                    return;
//                }
//        }, cToken);

//        uint connectionId = 0;
//        tcpListener.Start();
//        _logger.LogInformation("Started RCON server");

//        while (!cToken.IsCancellationRequested)
//            try
//            {
//                var conn = await tcpListener.AcceptTcpClientAsync(cToken);
//                _logger.LogInformation("Accepting RCON connection ID {ConnectionId} from {RemoteAddress}", ++connectionId, conn.Client.RemoteEndPoint as IPEndPoint);
//                _connections.Add(new RconConnection(connectionId, conn, _logger, data, cToken));
//            }
//            catch (Exception e) when (e is TaskCanceledException or OperationCanceledException)
//            {
//                break;
//            }

//        _logger.LogInformation("Stopping RCON server");
//        tcpListener.Stop();
//    }

//    private InitData GenerateKeys(Server server)
//    {
//        string password = _config.Value.Rcon?.Password ??
//            throw new InvalidOperationException("You can't start a RconServer without setting a password in the configuration.");


//        var gen = new DHParametersGenerator();
//        gen.Init(KEY_SIZE, CERTAINTY, new SecureRandom());
//        var dhParameters = gen.GenerateParameters();

//        var keyGen = GeneratorUtilities.GetKeyPairGenerator("DH");
//        var kgp = new DHKeyGenerationParameters(new SecureRandom(), dhParameters);
//        keyGen.Init(kgp);
//        var keyPair = keyGen.GenerateKeyPair();

//        return new InitData(server, 
//            _cmdHandler, 
//            password,
//            _config.Value.Rcon.RequireEncryption,
//            dhParameters,
//            keyPair);
//    }

//    public record InitData(
//    Server Server, CommandHandler CommandHandler, string Password, bool RequireEncryption,
//    DHParameters? DhParameters, AsymmetricCipherKeyPair? KeyPair);

//}


