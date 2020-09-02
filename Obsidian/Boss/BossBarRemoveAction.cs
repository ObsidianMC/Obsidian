using System;
using System.Threading.Tasks;

namespace Obsidian.Boss
{
    public class BossBarRemoveAction : BossBarAction
    {
        public override int Action => 1;

        public override Task<byte[]> ToArrayAsync() => Task.FromResult(Array.Empty<byte>());
    }
}
