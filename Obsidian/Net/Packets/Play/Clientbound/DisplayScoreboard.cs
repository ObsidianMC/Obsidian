using Obsidian.API;
using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class DisplayScoreboard : IPacket
    {
        [Field(0), ActualType(typeof(sbyte))]
        public ScoreboardPosition Position { get; set; }

        [Field(1)]
        public string ScoreName { get; set; }

        public int Id => 0x43;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;

        public Task ReadAsync(MinecraftStream stream) => Task.CompletedTask;

        public Task WriteAsync(MinecraftStream stream) => Task.CompletedTask;
    }
}
