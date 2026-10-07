using Microsoft.Extensions.Logging;
using Obsidian.Integrated;
using Obsidian.Net.Packets.Play.Clientbound;
using System.Collections.Immutable;
using System.IO;

namespace Obsidian.Utilities;

public sealed partial class OperatorList : IOperatorList
{
    private readonly List<Operator> operators = [];
    private readonly Dictionary<string, OperatorRequest> requests = [];
    private readonly IServer server;
    private readonly ILogger _logger;
    private readonly IntegratedSession? integrated;
    private static string OpsFilePath => Path.Combine(ServerConstants.ConfigPath, "ops.json");

    /// <param name="integrated">An integrated server's session: its operators are kept in memory only, and its players
    /// get the levels the session gives them.</param>
    public OperatorList(IServer server, ILoggerFactory loggerFactory, IntegratedSession? integrated = null)
    {
        this.server = server;
        this.integrated = integrated;
        _logger = loggerFactory.CreateLogger<OperatorList>();
    }

    public async Task InitializeAsync()
    {
        if (this.integrated is not null)
            return;

        var fi = new FileInfo(OpsFilePath);
        fi.Directory?.Create();

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
            Log.RequestAlreadyPending(_logger, player.Username);
            return false;
        }

        var request = new OperatorRequest(player);
        requests.Add(request.Code, request);

        Log.RequestCreated(_logger, player.Username, request.Code);

        return true;
    }

    public bool ProcessRequest(IPlayer player, string code)
    {
        if (!this.requests.TryGetValue(code, out var request))
            return false;

        if (!requests.Remove(request.Code))
        {
            Log.RequestFailed(_logger, code);
            return false;
        }

        AddOperator(player);

        return true;
    }

    public void AddOperator(IPlayer player, int level = 4, bool bypassesPlayerLimit = false)
    {
        this.operators.Add(new Operator { Username = player.Username, Uuid = player.Uuid, Level = level, BypassesPlayerLimit = bypassesPlayerLimit  });
        this.UpdateList();
        _ = this.SendPermissionLevelAsync(player);
    }

    public void RemoveOperator(IPlayer player)
    {
        this.operators.RemoveAll(x => x.Uuid == player.Uuid);

        this.UpdateList();
        _ = this.SendPermissionLevelAsync(player);
    }

    public bool IsOperator(IPlayer player) => this.GetPermissionLevel(player) > 0;

    /// <summary>
    /// A player's permission level, 0 to 4: their operator level, or on an integrated server the level its session
    /// gives them when that's higher.
    /// </summary>
    public int GetPermissionLevel(IPlayer player)
    {
        var level = this.operators.Where(x => x.Uuid == player.Uuid).Select(x => x.Level).DefaultIfEmpty(0).Max();

        if (this.integrated is not null)
            level = Math.Max(level, this.integrated.GetPermissionLevel(player, this.server.DefaultWorld.LevelData.AllowCommands));

        return level;
    }

    /// <summary>
    /// Tells an online player their permission level and resends the commands, after it changed.
    /// </summary>
    public async Task SendPermissionLevelAsync(IPlayer player)
    {
        if (!this.server.IsPlayerOnline(player.Uuid))
            return;

        try
        {
            await player.Client.QueuePacketAsync(EntityEventPacket.PermissionLevel(player.EntityId, this.GetPermissionLevel(player)));
            await player.Client.QueuePacketAsync(CommandsRegistry.Packet);
        }
        catch (OperationCanceledException)
        {
            // The player disconnected meanwhile.
        }
    }

    public ImmutableList<IPlayer> GetOnlineOperators() => server.OnlinePlayers.Values.Where(IsOperator).ToImmutableList();

    private static partial class Log
    {
        [LoggerMessage(Level = LogLevel.Information, Message = "Operator request from {Username}: {Code}")]
        public static partial void RequestCreated(ILogger logger, string username, string code);

        [LoggerMessage(Level = LogLevel.Debug, Message = "{Username} already has a pending operator request")]
        public static partial void RequestAlreadyPending(ILogger logger, string username);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to process the operator request with code {Code}")]
        public static partial void RequestFailed(ILogger logger, string code);
    }

    private void UpdateList()
    {
        if (this.integrated is null)
            File.WriteAllText(OpsFilePath, operators.ToJson());
    }

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
