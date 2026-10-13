namespace Obsidian.API;
public sealed record class JukeboxSong : INetworkSerializable<JukeboxSong>
{
    public SoundEvent SoundEvent { get; init; }

    public ChatMessage Description
    {
        get => Inventory.DataComponents.ComponentCopies.Chat(field)!;
        init => field = Inventory.DataComponents.ComponentCopies.Chat(value)!;
    }

    public float LengthInSeconds { get; init; }

    public int ComparatorOutput { get; init; }

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
