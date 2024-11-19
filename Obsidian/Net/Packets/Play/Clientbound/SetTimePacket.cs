using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class SetTimePacket
{
    [Field(0)]
    public long WorldAge { get; }

    [Field(1)]
    public long TimeOfDay { get; }
    public SetTimePacket(long worldAge, long timeOfDay)
    {
        WorldAge = worldAge;
        TimeOfDay = timeOfDay;
    }
}
