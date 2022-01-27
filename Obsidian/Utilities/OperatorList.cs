using Microsoft.Extensions.Logging;
using System.IO;

namespace Obsidian.Utilities;

public class OperatorList : IOperatorList
{
    private List<Operator> ops;
    private readonly List<OperatorRequest> reqs;
    private readonly Server server;
    private string Path => System.IO.Path.Combine(server.ServerFolderPath, "ops.json");

    public OperatorList(Server server)
    {
        ops = new List<Operator>();
        reqs = new List<OperatorRequest>();
        this.server = server;
    }

    public async Task InitializeAsync()
    {
        var fi = new FileInfo(Path);

        if (fi.Exists)
        {
            using var fs = fi.OpenRead();
            ops = await fs.FromJsonAsync<List<Operator>>();
        }
        else
        {
            using var fs = fi.Create();

            await ops.ToJsonAsync(fs);
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

            server.Logger.LogWarning($"New operator request from {p.Username}: {req.Code}");
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
        ops.Add(new Operator { Username = username, Uuid = Guid.Empty });
        UpdateList();
    }

    public void RemoveOperator(IPlayer p)
    {
        ops.RemoveAll(x => x.Uuid == p.Uuid || x.Username == p.Username);
        UpdateList();
    }

    public void RemoveOperator(string value)
    {
        ops.RemoveAll(x => x.Username == value || x.Uuid == Guid.Parse(value));
        UpdateList();
    }

    public bool IsOperator(IPlayer p) => ops.Any(x => x.Username == p.Username || p.Uuid == x.Uuid);

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
