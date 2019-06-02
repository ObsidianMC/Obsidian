using Obsidian.PlayerInfo;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
            using (var stream = new MemoryStream())
            {
                await stream.WriteVarIntAsync(Action);
                await stream.WriteVarIntAsync(Actions.Count);
                foreach (PlayerInfoAction action in Actions)
                {
                    await stream.WriteAsync(await action.ToArrayAsync());
                }
                return stream.ToArray();
            }
        }

        protected override Task PopulateAsync() => throw new NotImplementedException();
    }
}
