using Microsoft.Extensions.Logging;
using Obsidian.API.Logging;
using System.Collections.Immutable;
using System.IO;

namespace Obsidian.Utilities;

public class OperatorList : IOperatorList
{
    private List<Operator> ops;
    private readonly List<OperatorRequest> reqs;
    private readonly Server server;
    private readonly ILogger _logger;
    private string Path => System.IO.Path.Combine(System.IO.Path.Combine(this.server.ServerFolderPath, "config"), "ops.json");

    public OperatorList(Server server)
    {
        this.ops = new List<Operator>();
        this.reqs = new List<OperatorRequest>();
        this.server = server;
        var loggerProvider = new LoggerProvider();
        _logger = loggerProvider.CreateLogger("OperatorList");
    }

    public async Task InitializeAsync()
    {
        var fi = new FileInfo(this.Path);

        if (fi.Exists)
        {
            await using var fs = fi.OpenRead();
            this.ops = await fs.FromJsonAsync<List<Operator>>();
        }
        else
        {
            await using var fs = fi.Create();

            await this.ops.ToJsonAsync(fs);
        }
    }

    public void AddOperator(IPlayer p)
    {
        ops.Add(new Operator { Username = p.Username, Uuid = p.Uuid });

        UpdateList();
    }

    public bool CreateRequest(IPlayer p)
    {
        if (!server.Config.AllowOperatorRequests)
        {
            return false;
        }

        var result = reqs.All(r => r.Player != p);

        if (result)
        {
            var req = new OperatorRequest(p);
            reqs.Add(req);

            _logger.LogWarning($"New operator request from {p.Username}: {req.Code}");
        }

        return result;
    }

    public bool ProcessRequest(IPlayer p, string code)
    {
        var result = reqs.FirstOrDefault(r => r.Player == p && r.Code == code);

        if (result == null)
        {
            return false;
        }

        reqs.Remove(result);

        AddOperator(p);

        return true;
    }

    public void AddOperator(string username)
    {
        this.ops.Add(new Operator { Username = username, Uuid = Guid.Empty });
        this.UpdateList();
    }

    public void RemoveOperator(IPlayer p)
    {
        this.ops.RemoveAll(x => x.Uuid == p.Uuid || x.Username == p.Username);
        this.UpdateList();
    }

    public void RemoveOperator(string value)
    {
        this.ops.RemoveAll(x => x.Username == value || x.Uuid == Guid.Parse(value));
        this.UpdateList();
    }

    public bool IsOperator(IPlayer p) => this.ops.Any(x => x.Username == p.Username || p.Uuid == x.Uuid);

    public ImmutableList<IPlayer> GetOnlineOperators() => server.Players.Where(IsOperator).ToImmutableList();

    private void UpdateList()
    {
        File.WriteAllText(Path, ops.ToJson());
    }

    private class Operator
    {
        public string Username { get; set; }

        public Guid Uuid { get; set; }
    }

    private class OperatorRequest
    {
        public IPlayer Player;
        public string Code;

        public OperatorRequest(IPlayer player)
        {
            Player = player ?? throw new ArgumentNullException(nameof(player));

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
