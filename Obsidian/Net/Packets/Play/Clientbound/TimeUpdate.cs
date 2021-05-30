using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Clientbound
{
    [ClientOnly]
    public partial class TimeUpdate : IClientboundPacket
    {
        [Field(0)]
        public long WorldAge { get; set; }

        [Field(1)]
        public long TimeOfDay { get; set; }

        public int Id => 0x4E;

        public TimeUpdate(long worldAge, long timeOfDay)
        {
            WorldAge = worldAge;
            TimeOfDay = timeOfDay;
        }

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}