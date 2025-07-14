using Microsoft.Extensions.Logging;
using System.Collections.Immutable;
using System.IO;

namespace Obsidian.Utilities;

public sealed class OperatorList : IOperatorList
{
    private readonly List<Operator> operators = [];
    private readonly Dictionary<string, OperatorRequest> requests = [];
    private readonly IServer server;
    private readonly ILogger _logger;
    private static string OpsFilePath => Path.Combine("config", "ops.json");

    public OperatorList(IServer server, ILoggerFactory loggerFactory)
    {
        this.server = server;
        _logger = loggerFactory.CreateLogger<OperatorList>();
    }

    public async Task InitializeAsync()
    {
        var fi = new FileInfo(OpsFilePath);

        if (fi.Exists)
        {
            await using var fs = fi.OpenRead();
            var ops = await fs.FromJsonAsync<List<Operator>>();

            this.operators.AddRange(ops!);
        }
        else
        {
            await using var fs = fi.Create();

            await this.operators.ToJsonAsync(fs);
        }
    }

    public bool CreateRequest(IPlayer player)
    {
        if (!server.Configuration.AllowOperatorRequests)
            return false;

        if (this.requests.Values.Any(x => x.Player == player))
        {
            _logger.LogWarning("{Username} tried to put in another request but already has one pending.", player.Username);
            return false;
        }

        var request = new OperatorRequest(player);
        requests.Add(request.Code, request);

        _logger.LogInformation("New operator request from {Username}: {Code}", player.Username, request.Code);

        return true;
    }

    public bool ProcessRequest(IPlayer player, string code)
    {
        if (!this.requests.TryGetValue(code, out var request))
            return false;

        if (!requests.Remove(request.Code))
        {
            _logger.LogWarning("Failed to process request with code: {code}", code);
            return false;
        }

        AddOperator(player);

        return true;
    }

    public void AddOperator(IPlayer player, int level = 4, bool bypassesPlayerLimit = false)
    {
        this.operators.Add(new Operator { Username = player.Username, Uuid = player.Uuid, Level = level, BypassesPlayerLimit = bypassesPlayerLimit  });
        this.UpdateList();
    }

    public void RemoveOperator(IPlayer player)
    {
        this.operators.RemoveAll(x => x.Uuid == player.Uuid);

        this.UpdateList();
    }

    public bool IsOperator(IPlayer player) => this.operators.Any(x => x.Uuid == player.Uuid);

    public ImmutableList<IPlayer> GetOnlineOperators() => server.OnlinePlayers.Values.Where(IsOperator).ToImmutableList();

    private void UpdateList() =>
        File.WriteAllText(OpsFilePath, operators.ToJson());

    private readonly struct Operator
    {
        public required string Username { get; init; }

        public required Guid Uuid { get; init; }

        public required int Level { get; init; }

        public required bool BypassesPlayerLimit { get; init; }
    }

    private readonly struct OperatorRequest
    {
        public IPlayer Player { get; }
        public string Code { get; }


        public OperatorRequest(IPlayer player)
        {
            ArgumentNullException.ThrowIfNull(player);

            Player = player;

            static string GetCode()
            {
                var random = Globals.Random;
                const int codeLength = 10;
                const string chars = "0123456789ABCDEFabcdef";
                var code = new char[codeLength];
                for (int i = 0; i < codeLength; i++)
                {
                    code[i] = chars[random.Next(0, chars.Length - 1)];
                }

                return new string(code);
            }

            Code = GetCode();
        }
    }
}
