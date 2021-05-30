using Obsidian.Entities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public interface IPacket
    {
        public int Id { get; }

        public Task HandleAsync(Server server, Player player);
    }
}