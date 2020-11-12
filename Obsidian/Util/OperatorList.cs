using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Obsidian.API;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.Util
{
    public class OperatorList : IOperatorList
    {
        private List<Operator> ops;
        private readonly List<OperatorRequest> reqs;
        private readonly Server server;
        private string Path => System.IO.Path.Combine(this.server.ServerFolderPath, "ops.json");

        public OperatorList(Server server)
        {
            this.ops = new List<Operator>();
            this.reqs = new List<OperatorRequest>();
            this.server = server;
        }

        public async Task InitializeAsync()
        {
            var fi = new FileInfo(this.Path);
            if (fi.Exists)
            {
                this.ops = JsonConvert.DeserializeObject<List<Operator>>(File.ReadAllText(Path));
            }
            else
            {
                using var sw = fi.CreateText();

                await sw.WriteAsync(JsonConvert.SerializeObject(this.ops, Formatting.Indented));
            }
        }

        public void AddOperator(IPlayer p)
        {
            ops.Add(new Operator() { Username = p.Username, Uuid = p.Uuid });

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
            this.ops.Add(new Operator() { Username = username, Uuid = Guid.Empty });
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

        private void UpdateList()
        {
            File.WriteAllText(Path, JsonConvert.SerializeObject(ops, Formatting.Indented));
        }

        private class Operator
        {
            [JsonProperty("username")]
            public string Username { get; set; }

            [JsonProperty("uuid")]
            public Guid Uuid { get; set; }
        }

        private class OperatorRequest
        {
            public IPlayer Player;
            public string Code;

            public OperatorRequest(IPlayer player)
            {
                Player = player ?? throw new ArgumentNullException(nameof(player));

                string GetCode()
                {
                    string code = "";
                    var random = new Random();
                    const string chars = "0123456789ABCDEFabcdef";
                    for (int i = 0; i < 10; i++)
                    {
                        code += chars[random.Next(0, chars.Length - 1)];
                    }

                    return code;
                }

                Code = GetCode();
            }
        }
    }
}