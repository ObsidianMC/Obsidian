using Obsidian.Entities;
using Obsidian.Net.Actions.PlayerInfo;
using Obsidian.Serialization.Attributes;

using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class PlayerInfo : ISerializablePacket
    {
        [Field(0), VarLength]
        public int Action { get; }

        [Field(1)]
        public List<PlayerInfoAction> Actions { get; }

        public int Id => 0x32;

        public PlayerInfo(int action, List<PlayerInfoAction> actions)
        {
            Action = action;
            Actions = actions;
        }

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}