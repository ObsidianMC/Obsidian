namespace Obsidian.Entities;

public class Avatar : Living
{
    /// <summary>
    /// The client's settings, from its client information packet; until it sends one, vanilla's defaults
    /// (<c>ClientInformation.createDefault</c>).
    /// </summary>
    public ClientInformation ClientInformation { get; set; } = new()
    {
        Language = "en_us",
        ViewDistance = 2,
        ChatVisibility = ChatVisibility.Full,
        ChatColors = true,
        ModelCustomisation = 0,
        MainHand = HumanoidArm.Right,
        TextFilteringEnabled = false,
        AllowsListing = false,
        ParticleStatus = ParticleStatus.All
    };

    public override void Write(INetStreamWriter writer)
    {
        base.Write(writer);

        writer.WriteEntityMetadataType(15, EntityMetadataType.HumanoidArm);
        writer.WriteVarInt(ClientInformation.MainHand);

        writer.WriteEntityMetadataType(16, EntityMetadataType.Byte);
        writer.WriteByte((byte)ClientInformation.ModelCustomisation);

        this.MetadataIndex = 17;
    }
}
