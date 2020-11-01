using System;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IServer
    {
        public string Version { get; }
        public int Port { get; }
        public short TPS { get; }
        public DateTimeOffset StartTime { get; }
        public bool IsPlayerOnline(string username);
        public bool IsPlayerOnline(Guid uuid);
        public Task BroadcastAsync(string message, sbyte position = 0);
        public IPlayer GetPlayer(string username);
        public IPlayer GetPlayer(Guid uuid);
    }
}
