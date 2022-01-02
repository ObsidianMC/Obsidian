using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class EncryptionRequest : IClientboundPacket
{
    [Field(0)]
    public string ServerId { get; } = string.Empty;

    [Field(1), VarLength]
    public int PublicKeyLength { get => PublicKey.Length; }

    [Field(2)]
    public byte[] PublicKey { get; }

    [Field(3), VarLength]
    public int VerifyTokenLength { get => VerifyToken.Length; }

    [Field(4)]
    public byte[] VerifyToken { get; }

    public int Id => 0x01;

    public EncryptionRequest(byte[] publicKey, byte[] verifyToken)
    {
        PublicKey = publicKey;
        VerifyToken = verifyToken;
    }
}
