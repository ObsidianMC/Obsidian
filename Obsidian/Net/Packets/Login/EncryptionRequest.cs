using Obsidian.Util;

namespace Obsidian.Net.Packets
{
    public class EncryptionRequest : Packet
    {
        [Variable(VariableType.String)]
        public string ServerId { get; private set; }

        [Variable]
        public int KeyLength { get { return this.PublicKey.Length; } set { } }

        [Variable]
        public byte[] PublicKey { get; private set; }

        [Variable]
        public int TokenLength = 4;

        [Variable]
        public byte[] VerifyToken { get; private set; }

        public EncryptionRequest(byte[] publicKey, byte[] verifyToken) : base(0x01, new byte[0])
        {
            this.PublicKey = publicKey;

            this.VerifyToken = verifyToken;
        }
    }
}