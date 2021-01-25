using System;
using System.Threading.Tasks;

namespace Obsidian.Net.Actions.BossBar
{
    public class BossBarRemoveAction : BossBarAction
    {
        public override int Action => 1;

        public override byte[] ToArray() => Array.Empty<byte>();

        public override Task<byte[]> ToArrayAsync() => Task.FromResult(Array.Empty<byte>());
    }
}
