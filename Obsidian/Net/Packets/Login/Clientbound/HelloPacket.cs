using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Clientbound;

public partial class HelloPacket
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

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteString(this.ServerId);

        writer.WriteVarInt(this.PublicKeyLength);
        writer.WriteByteArray(this.PublicKey);

        writer.WriteVarInt(this.VerifyTokenLength);
        writer.WriteByteArray(this.VerifyToken);

        writer.WriteBoolean(this.ShouldAuthenticate);
    }
}
