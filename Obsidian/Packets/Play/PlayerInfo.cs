using Obsidian.PlayerInfo;
using Obsidian.Util;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Obsidian.Packets.Play
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
                {
                    var array = await action.ToArrayAsync();
                    await stream.WriteAsync(array);
                }
                return stream.ToArray();
            }
        }

        protected override Task PopulateAsync() => throw new NotImplementedException();
    }
}
