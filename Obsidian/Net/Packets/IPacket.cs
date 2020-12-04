using Obsidian.Entities;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public interface IPacket
    {
        int Id { get; }

        Task WriteAsync(MinecraftStream stream);

        Task ReadAsync(MinecraftStream stream);

        Task HandleAsync(Server server, Player player);

        public void Serialize(MinecraftStream stream);
    }
}