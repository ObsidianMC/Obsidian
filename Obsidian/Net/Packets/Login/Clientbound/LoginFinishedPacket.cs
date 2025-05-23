using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Login.Clientbound;

public partial class LoginFinishedPacket(Guid uuid, string username)
{
    [Field(0)]
    public Guid Uuid { get; } = uuid;

    [Field(1)]
    public string Username { get; } = username;

    [Field(3)]
    public List<SkinProperty> SkinProperties { get; init; } = [];

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteUuid(Uuid);
        writer.WriteString(Username);
        
        writer.WriteVarInt(SkinProperties.Count);
        foreach(var skinProperty in SkinProperties)
        {
            writer.WriteString(skinProperty.Name);
            writer.WriteString(skinProperty.Value);

            writer.WriteOptional(skinProperty.Signature);
        }
    }
}
