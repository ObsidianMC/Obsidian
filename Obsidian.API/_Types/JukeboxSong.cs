namespace Obsidian.API;
public sealed record class JukeboxSong : INetworkSerializable<JukeboxSong>
{
    public SoundEvent SoundEvent { get; set; }

    public ChatMessage Description { get; set; }

    public float LengthInSeconds { get; set; }

    public int ComparatorOutput { get; set; }

    public static JukeboxSong Read(INetStreamReader reader) => new()
    {
        SoundEvent = reader.ReadSoundEvent(),
        Description = reader.ReadChat(),
        LengthInSeconds = reader.ReadSingle(),
        ComparatorOutput = reader.ReadVarInt()
    };

    public static void Write(JukeboxSong value, INetStreamWriter writer)
    {
        writer.WriteSoundEvent(value.SoundEvent);
        ChatMessage.Write(value.Description, writer);
        writer.WriteSingle(value.LengthInSeconds);
        writer.WriteVarInt(value.ComparatorOutput);
    }
}
