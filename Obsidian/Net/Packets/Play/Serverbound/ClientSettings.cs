using Obsidian.Entities;
using Obsidian.Serialization.Attributes;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets.Play.Serverbound
{
    [ServerOnly]
    public partial class ClientSettings : IServerboundPacket
    {
        [Field(0)]
        public string Locale { get; private set; }

        [Field(1)]
        public sbyte ViewDistance { get; private set; }

        [Field(2)]
        public int ChatMode { get; private set; }

        [Field(3)]
        public bool ChatColors { get; private set; }

        [Field(4)]
        public byte SkinParts { get; private set; } // skin parts that are displayed. might not be necessary to decode?

        [Field(5)]
        public int MainHand { get; private set; }

        public int Id => 0x05;

        public Task HandleAsync(Server server, Player player) => Task.CompletedTask;
    }
}