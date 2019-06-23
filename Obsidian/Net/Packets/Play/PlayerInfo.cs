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

        public override async Task<byte[]> ToArrayAsync()
        {
            using (var stream = new MinecraftStream())
            {
                await stream.WriteVarIntAsync(Action);
                await stream.WriteVarIntAsync(Actions.Count);
                foreach (PlayerInfoAction action in Actions)
                    await stream.WriteAsync(await action.ToArrayAsync());
                
                return stream.ToArray();
            }
        }

        public override Task PopulateAsync() => throw new NotImplementedException();
    }
}
