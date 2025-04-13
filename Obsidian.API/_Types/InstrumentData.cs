namespace Obsidian.API;
public sealed record class InstrumentData : INetworkSerializable<InstrumentData>
{
    public SoundEvent SoundEvent { get; set; }

    public float UseDuration { get; set; }

    public float Range { get; set; }
    
    public ChatMessage Description { get; set; }

    public static InstrumentData Read(INetStreamReader reader) => new()
    {
        SoundEvent = reader.ReadSoundEvent(),
        UseDuration = reader.ReadSingle(),
        Range = reader.ReadSingle(),
        Description = reader.ReadChat()
    };

    public static void Write(InstrumentData value, INetStreamWriter writer)
    {
        writer.WriteSoundEvent(value.SoundEvent);
        writer.WriteSingle(value.UseDuration);
        writer.WriteSingle(value.Range);
        writer.WriteChat(value.Description);
    }
}
