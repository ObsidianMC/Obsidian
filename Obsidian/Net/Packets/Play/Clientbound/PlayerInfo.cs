using Obsidian.Entities;
using Obsidian.PlayerData.Info;
using Obsidian.Serializer.Attributes;
using Obsidian.Serializer.Enums;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    public class PlayerInfo : IPacket
    {
        [Field(0, Type = DataType.VarInt)]
        public int Action { get; }


        [Field(1, Type = DataType.Array)]
        public List<PlayerInfoAction> Actions { get; }

        public int Id => 0x32;

        public byte[] Data { get; }

        public PlayerInfo() { }

        public PlayerInfo(int action, List<PlayerInfoAction> actions)
        {
            this.Action = action;
            this.Actions = actions;
        }

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task HandleAsync(Obsidian.Server server, Player player) => Task.CompletedTask;
    }
}