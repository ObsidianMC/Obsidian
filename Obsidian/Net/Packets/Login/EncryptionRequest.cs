using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class EncryptionRequest : IClientboundPacket
{
    [Field(0)]
    public string ServerId { get; init; } = string.Empty;

    [Field(1), VarLength]
    public int PublicKeyLength => PublicKey.Length;

    [Field(2)]
    public required byte[] PublicKey { get; init; }

    [Field(3), VarLength]
    public int VerifyTokenLength => VerifyToken.Length;

    [Field(4)]
    public required byte[] VerifyToken { get; init; }

    [Field(5)]
    public required bool ShouldAuthenticate { get; init; }

    public int Id => 0x01;
}
