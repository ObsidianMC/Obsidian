using Newtonsoft.Json;
using Obsidian.Entities;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace Obsidian.Util
{
    public class OperatorList
    {
        private List<Operator> _ops;
        private Server _server;

        public OperatorList(Server s)
        {
            _ops = new List<Operator>();
            _server = s;
        }

        public void Initialize()
        {
            if (!File.Exists("ops.json"))
            {
                using (var opfile = File.CreateText("ops.json"))
                {
                    opfile.Write(JsonConvert.SerializeObject(_ops));
                }
            }
            else
            {
                _ops = JsonConvert.DeserializeObject<List<Operator>>(File.ReadAllText("ops.json"));
            }
        }

        public void AddOperator(Player p)
        {
            _ops.Add(new Operator() { Username = p.Username, UUID = _server.Config.OnlineMode ? p?.UUID : null });
            _updateList();
        }

        public void RemoveOperator(Player p)
        {
            _ops.RemoveAll(x => x.UUID == p.UUID || x.Username == p.Username);
            _updateList();
        }

        public bool IsOperator(Player p)
        {
            return _ops.Any(x => 
                (x.Username == p.Username || p.UUID == x.UUID)
                 && x.Online == _server.Config.OnlineMode
                 );
        }

        private void _updateList()
        {
            File.WriteAllText("ops.json", JsonConvert.SerializeObject(_ops));
        }

        // we only use this in this class
        private class Operator
        {
            [JsonProperty("username")]
            public string Username;

            [JsonProperty("uuid")]
            public Guid? UUID;

            [JsonIgnore]
            public bool Online => UUID != null;
        }
    }
}
