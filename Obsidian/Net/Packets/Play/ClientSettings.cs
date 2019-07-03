using Obsidian.Util;
using System.Threading.Tasks;

namespace Obsidian.Net.Packets
{
    public class ClientSettings : Packet
    {
        public ClientSettings(byte[] data) : base(0x04, data)
        {
            
        }

        [Variable(VariableType.String)]
        public string Locale { get; private set; }

        [Variable(VariableType.Byte)]
        public sbyte ViewDistance { get; private set; }

        [Variable(VariableType.Int)]
        public int ChatMode { get; private set; }

        [Variable(VariableType.Boolean)]
        public bool ChatColors { get; private set; }

        [Variable(VariableType.UnsignedByte)]
        public byte SkinParts { get; private set; } // skin parts that are displayed. might not be necessary to decode?

        [Variable(VariableType.VarInt)]
        public int MainHand { get; private set; }
    }
}