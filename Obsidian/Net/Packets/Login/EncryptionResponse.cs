using Obsidian.Entities;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class EncryptionResponse : IServerboundPacket
{
    [Field(0)]
    public byte[] SharedSecret { get; private set; }

    [Field(1)]
    public bool HasVerifyToken { get; private set; }

    [Field(2), Condition("HasVerifyToken")]
    public byte[] VerifyToken { get; private set; }

    [Field(3), Condition("!HasVerifyToken")]
    public long Salt { get; private set; }

    [Field(4), Condition("!HasVerifyToken")]
    public byte[] MessageSignature { get; private set; }

    public int Id => 0x01;

    public ValueTask HandleAsync(Server server, Player player) => ValueTask.CompletedTask;  
}
