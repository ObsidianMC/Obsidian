using Newtonsoft.Json;
using Obsidian.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Obsidian.Util
{
    public class OperatorList
    {
        private List<Operator> _ops;
        private readonly List<OperatorRequest> _reqs;
        private readonly Server _server;
        private string Path => System.IO.Path.Combine(this._server.ServerFolderPath, "ops.json");

        public OperatorList(Server s)
        {
            _ops = new List<Operator>();
            _reqs = new List<OperatorRequest>();
            _server = s;
        }

        public void Initialize()
        {
            if (!File.Exists(Path))
            {
                using var opfile = File.CreateText(Path);

                opfile.Write(JsonConvert.SerializeObject(_ops));
            }
            else
            {
                _ops = JsonConvert.DeserializeObject<List<Operator>>(File.ReadAllText(Path));
            }
        }

        public void AddOperator(Player p)
        {
            _ops.Add(new Operator() { Username = p.Username, Uuid = p.Uuid });

            _updateList();
        }

        public bool CreateRequest(Player p)
        {
            if (!_server.Config.AllowOperatorRequests)
            {
                return false;
            }

            var result = _reqs.All(r => r.Player != p);

            if (result)
            {
                var req = new OperatorRequest(p);
                _reqs.Add(req);

                _server.Logger.LogWarningAsync($"New operator request from {p.Username}: {req.Code}");
            }

            return result;
        }

        public bool ProcessRequest(Player p, string code)
        {
            var result = _reqs.FirstOrDefault(r => r.Player == p && r.Code == code);

            if (result == null)
            {
                return false;
            }

            _reqs.Remove(result);

            AddOperator(p);

            return true;
        }

        public void AddOperator(string username)
        {
            _ops.Add(new Operator() { Username = username, Uuid = Guid.Empty });
            _updateList();
        }

        public void RemoveOperator(Player p)
        {
            _ops.RemoveAll(x => x.Uuid == p.Uuid || x.Username == p.Username);
            _updateList();
        }

        public void RemoveOperator(string value)
        {
            _ops.RemoveAll(x => x.Username == value || x.Uuid == Guid.Parse(value));
            _updateList();
        }

        public bool IsOperator(Player p)
        {
            return _ops.Any(x =>
                (x.Username == p.Username || p.Uuid == x.Uuid)
                 && x.Online == _server.Config.OnlineMode
                 );
        }

        private void _updateList()
        {
            File.WriteAllText(Path, JsonConvert.SerializeObject(_ops));
        }

        // we only use this in this class
        private class Operator
        {
            [JsonProperty("username")]
            public string Username;

            [JsonProperty("uuid")]
            public Guid Uuid;

            [JsonIgnore]
            public bool Online => Uuid != null;
        }

        private class OperatorRequest
        {
            public Player Player;
            public string Code;

            public OperatorRequest(Player player)
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