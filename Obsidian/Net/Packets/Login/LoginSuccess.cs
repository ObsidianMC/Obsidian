using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login;

public partial class LoginSuccess(Guid uuid, string username) : IClientboundPacket
{
    [Field(0)]
    public Guid UUID { get; } = uuid;

    [Field(1)]
    public string Username { get; } = username;

    [Field(3)]
    public List<SkinProperty> SkinProperties { get; init; } = new();

    [Field(4)]
    [Obsolete]
    public bool StrictErrorHandling { get; init; }

    public int Id => 0x02;
}
