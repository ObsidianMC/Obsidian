using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetHealthPacket(float health, int food, float saturation)
{
    [Field(0)]
    public float Health { get; } = health;

    [Field(1), VarLength]
    public int Food { get; } = food;

    [Field(2)]
    public float Saturation { get; } = saturation;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteSingle(this.Health);

        writer.WriteVarInt(this.Food);

        writer.WriteSingle(this.Saturation);
    }
}
