namespace Obsidian.API;
/// <summary>A song a jukebox can play from a music disc.</summary>
public sealed record class JukeboxSong : INetworkSerializable<JukeboxSong>
{
    /// <summary>Gets the sound the song plays.</summary>
    public SoundEvent SoundEvent { get; init; }

    /// <summary>Gets the song's title, shown in the disc's tooltip and when it starts playing.</summary>
    public ChatMessage Description
    {
        get => Inventory.DataComponents.ComponentCopies.Chat(field)!;
        init => field = Inventory.DataComponents.ComponentCopies.Chat(value)!;
    }

    /// <summary>Gets the song's length, in seconds.</summary>
    public float LengthInSeconds { get; init; }

    /// <summary>
    /// Gets the signal strength, from 0 to 15, that a comparator reads from a jukebox playing the song.
    /// </summary>
    public int ComparatorOutput { get; init; }

    /// <summary>Reads a song from a network stream.</summary>
    public static JukeboxSong Read(INetStreamReader reader) => new()
    {
        SoundEvent = reader.ReadSoundEvent(),
        Description = reader.ReadChat(),
        LengthInSeconds = reader.ReadSingle(),
        ComparatorOutput = reader.ReadVarInt()
    };

    /// <summary>Writes a song to a network stream.</summary>
    public static void Write(JukeboxSong value, INetStreamWriter writer)
    {
        writer.WriteSoundEvent(value.SoundEvent);
        ChatMessage.Write(value.Description, writer);
        writer.WriteSingle(value.LengthInSeconds);
        writer.WriteVarInt(value.ComparatorOutput);
    }
}
