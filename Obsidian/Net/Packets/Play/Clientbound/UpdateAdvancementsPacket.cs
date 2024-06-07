using Obsidian.API.Advancements;
using Obsidian.Serialization.Attributes;

namespace Obsidian.Net.Packets.Play.Clientbound;

//TODO finish this packet PLEASE
public partial class UpdateAdvancementsPacket : IClientboundPacket
{
    [Field(0)]
    public bool Reset { get; set; }

    [Field(1)]
    public IDictionary<string, Advancement> Advancements { get; set; }

    [Field(2)]
    public List<string> RemovedAdvancements { get; set; }

    public int Id => 0x74;

    public void Serialize(MinecraftStream stream)
    {
        using var packetStream = new MinecraftStream();

        packetStream.WriteBoolean(this.Reset);

        packetStream.WriteAdvancements(this.Advancements);

        //Not sure what this is for
        packetStream.WriteVarInt(this.RemovedAdvancements.Count);
        foreach (var removed in this.RemovedAdvancements)
            packetStream.WriteString(removed);

        //Write progress for advancements
        foreach (var (name, advancement) in this.Advancements)
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
        stream.WriteVarInt(this.Id.GetVarIntLength() + (int)packetStream.Length);
        stream.WriteVarInt(this.Id);
        packetStream.Position = 0;
        packetStream.CopyTo(stream);
        stream.Lock.Release();
    }
}
