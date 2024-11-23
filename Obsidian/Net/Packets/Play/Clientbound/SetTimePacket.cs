using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetTimePacket(long worldAge, long timeOfDay, bool tickDayTime)
{
    [Field(0)]
    public long WorldAge { get; } = worldAge;

    [Field(1)]
    public long TimeOfDay { get; } = timeOfDay;

    [Field(2)]
    public bool TickDayTime { get; } = tickDayTime;

    public override void Serialize(INetStreamWriter writer)
    {
        writer.WriteLong(this.WorldAge);
        writer.WriteLong(this.TimeOfDay);
        writer.WriteBoolean(this.TickDayTime);
    }
}
