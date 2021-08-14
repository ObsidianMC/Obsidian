using Obsidian.API.Crafting;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.API
{
    public interface IServer
    {
        public string Version { get; }
        public int Port { get; }
        public short TPS { get; }
        public DateTimeOffset StartTime { get; }
        public ProtocolVersion Protocol { get; }
        public IEnumerable<IPlayer> Players { get; }
        public IOperatorList Operators { get; }
        public IWorld DefaultWorld { get; }
        public IConfig Configuration { get; }

        public IScoreboardManager ScoreboardManager { get; }

        public bool IsPlayerOnline(string username);
        public bool IsPlayerOnline(Guid uuid);
        public Task BroadcastAsync(string message, MessageType type = MessageType.Chat);
        public Task BroadcastAsync(ChatMessage message, MessageType type = MessageType.Chat);
        public IPlayer? GetPlayer(string username);
        public IPlayer? GetPlayer(Guid uuid);
        public IPlayer? GetPlayer(int entityId);
        public void RegisterRecipes(params IRecipe[] recipes);
    }
}
