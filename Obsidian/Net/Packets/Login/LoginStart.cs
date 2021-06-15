using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Login
{
    public partial class LoginStart : IServerboundPacket
    {
        [Field(0)]
        public string Username { get; private set; }

        public int Id => 0x00;

        public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;
    }
}