using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class RespawnPacket
{
    [Field(0)]
    public required CommonPlayerSpawnInfo CommonPlayerSpawnInfo { get; set; }

    [Field(1)]
    public DataKept DataKept { get; init; }

    public override void Serialize(INetStreamWriter writer)
    {
        CommonPlayerSpawnInfo.Write(this.CommonPlayerSpawnInfo, writer);

        writer.WriteByte(this.DataKept);
    }
}
