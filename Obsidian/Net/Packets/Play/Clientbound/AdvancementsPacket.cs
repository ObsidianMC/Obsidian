using Obsidian.API.Advancements;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

public partial class AdvancementsPacket : IClientboundPacket
{
    [Field(0)]
    public bool Reset { get; set; }

    [Field(1)]
    public IDictionary<string, Advancement> Advancements { get; set; }

    [Field(2)]
    public List<string> RemovedAdvancements { get; set; }

    public int Id => 0x63;

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteBoolean(Reset);

        packetStream.WriteAdvancements(Advancements);

        //Not sure what this is for
        packetStream.WriteVarInt(RemovedAdvancements.Count);
        foreach (var removed in RemovedAdvancements)
            packetStream.WriteString(removed);

        //Write progress for advancements
        foreach (var (name, advancement) in Advancements)
        {
            packetStream.WriteVarInt(advancement.Criteria.Count);

            foreach (var criteria in advancement.Criteria)
            {
                packetStream.WriteString(criteria.Identifier);

                packetStream.WriteBoolean(criteria.Achieved);

                if (criteria.Achieved)
                    packetStream.WriteLong(criteria.AchievedAt.Value.ToUnixTimeMilliseconds());
            }
        }

        stream.Lock.Wait();
        stream.WriteVarInt(Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(Id);
        packetStream.Position = 0;
        packetStream.CopyTo(stream);
        stream.Lock.Release();
    }
}
