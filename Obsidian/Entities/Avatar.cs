namespace Obsidian.Entities;

public class Avatar : Living
{
    public ClientInformation ClientInformation { get; set; }

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        this.WriteEntityMetadataType(writer, EntityMetadataType.HumanoidArm);
        writer.WriteVarInt(ClientInformation.MainHand);

        this.WriteEntityMetadataType(writer, EntityMetadataType.Byte);
        writer.WriteByte(ClientInformation.DisplayedSkinParts);
    }
}
