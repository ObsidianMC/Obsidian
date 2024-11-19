using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Clientbound;

public partial class LoginFinishedPacket(Guid uuid, string username)
{
    [Field(0)]
    public Guid UUID { get; } = uuid;

    [Field(1)]
    public string Username { get; } = username;

    [Field(3)]
    public List<SkinProperty> SkinProperties { get; init; } = new();

    [Field(4)]
    [Obsolete("This is a temporary field for modded servers. Will be removed soon.")]
    public bool StrictErrorHandling { get; init; }
}
