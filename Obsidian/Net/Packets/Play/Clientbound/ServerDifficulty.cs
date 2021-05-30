using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using Obsidian.WorldData;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class ServerDifficulty : IClientboundPacket
    {
        [Field(0), ActualType(typeof(byte))]
        public Difficulty Difficulty { get; }

        public int Id => 0x0D;

        public ServerDifficulty(Difficulty difficulty)
        {
            Difficulty = difficulty;
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}