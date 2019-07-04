using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class ClientSettings : Packet
    {
        public ClientSettings(byte[] data) : base(0x04, data)
        {
        }

        [Variable]
        public string Locale { get; private set; }

        [Variable]
        public sbyte ViewDistance { get; private set; }

        [Variable(VariableType.Int)]
        public int ChatMode { get; private set; }

        [Variable]
        public bool ChatColors { get; private set; }

        [Variable]
        public byte SkinParts { get; private set; } // skin parts that are displayed. might not be necessary to decode?

        [Variable]
        public int MainHand { get; private set; }
    }
}