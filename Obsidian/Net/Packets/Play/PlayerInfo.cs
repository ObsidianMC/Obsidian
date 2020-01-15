using Obsidian.PlayerData.Info;

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class PlayerInfo : Packet
    {
        public List<PlayerInfoAction> Actions { get; }

        public int Action { get; }

        public PlayerInfo(int action, List<PlayerInfoAction> actions) : base(0x30)
        {
            this.Action = action;
            this.Actions = actions;
        }

        protected override async Task ComposeAsync(MinecraftStream stream)
        {
            await stream.WriteVarIntAsync(Action);
            await stream.WriteVarIntAsync(Actions.Count);

            foreach (var action in Actions)
                await action.WriteAsync(stream);
        }

        protected override Task PopulateAsync(MinecraftStream stream) => throw new NotImplementedException();
    }
}