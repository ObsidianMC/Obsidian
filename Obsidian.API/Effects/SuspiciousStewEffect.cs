namespace Obsidian.API.Effects;
public sealed class SuspiciousStewEffect : INetworkSerializable<SuspiciousStewEffect>
{
    public required int EffectId { get; set; }

    public required int Duration { get; set; }

    public static SuspiciousStewEffect Read(INetStreamReader reader) => new()
    {
        EffectId = reader.ReadVarInt(),
        Duration = reader.ReadVarInt()
    };

    public static void Write(SuspiciousStewEffect value, INetStreamWriter writer)
    {
        writer.WriteVarInt(value.EffectId);
        writer.WriteVarInt(value.Duration);
    }
}
