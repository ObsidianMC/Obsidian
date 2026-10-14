using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

/// <summary>
/// Attaches a leashed entity's lead to its holder, or detaches it when <see cref="HolderId"/> is 0.
/// </summary>
public partial class SetEntityLinkPacket
{
    [Field(0)]
    public required int LeashedId { get; init; }

    [Field(1)]
    public required int HolderId { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteInt(this.LeashedId);
        writer.WriteInt(this.HolderId);
    }
}
