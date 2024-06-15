using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class UpdateTimePacket : IClientboundPacket
{
    [Field(0)]
    public long WorldAge { get; }

    [Field(1)]
    public long TimeOfDay { get; }

    public int Id => 0x64;

    public UpdateTimePacket(long worldAge, long timeOfDay)
    {
        WorldAge = worldAge;
        TimeOfDay = timeOfDay;
    }
}
