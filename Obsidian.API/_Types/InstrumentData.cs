using Obsidian.API.Registries;

namespace Obsidian.API;
public sealed record class InstrumentData : INetworkSerializable<InstrumentData>
{
    /// <summary>
    /// The registry id of a vanilla instrument (e.g. <c>minecraft:ponder_goat_horn</c>), or null for a custom one.
    /// Vanilla instruments are sent by registry id; custom ones are sent with their data.
    /// </summary>
    public string? Identifier { get; set; }

    public SoundEvent SoundEvent { get; set; }

    public float UseDuration { get; set; }

    public float Range { get; set; }

    public ChatMessage Description { get; set; }

    // Vanilla's InstrumentComponent: an either (true = holder, false = resource key) of an instrument holder, which is
    // its registry id + 1, or 0 followed by the instrument. The registry id indexes the instrument registry sent during
    // configuration, in InstrumentsRegistry order.
    public static InstrumentData Read(INetStreamReader reader)
    {
        if (!reader.ReadBoolean())
            return FromRegistry(reader.ReadString());

        var holder = reader.ReadVarInt();
        if (holder > 0)
            return FromRegistry(InstrumentsRegistry.All[holder - 1].Identifier);

        // Sound events are holders too: 0 means the sound event follows.
        var soundHolder = reader.ReadVarInt();
        return new()
        {
            SoundEvent = soundHolder == 0 ? reader.ReadSoundEvent() : new SoundEvent { ResourceLocation = string.Empty },
            UseDuration = reader.ReadSingle(),
            Range = reader.ReadSingle(),
            Description = reader.ReadChat()
        };
    }

    public static void Write(InstrumentData value, INetStreamWriter writer)
    {
        writer.WriteBoolean(true);

        var registered = InstrumentsRegistry.All.FirstOrDefault(instrument => instrument.Identifier == value.Identifier);
        if (registered is not null)
        {
            writer.WriteVarInt(registered.Id + 1);
            return;
        }

        writer.WriteVarInt(0);
        writer.WriteVarInt(0);
        writer.WriteSoundEvent(value.SoundEvent);
        writer.WriteSingle(value.UseDuration);
        writer.WriteSingle(value.Range);
        writer.WriteChat(value.Description);
    }

    private static InstrumentData FromRegistry(string identifier)
    {
        var instrument = InstrumentsRegistry.All.FirstOrDefault(instrument => instrument.Identifier == identifier);
        return instrument is not null ? instrument.ToInstrumentData() : new() { Identifier = identifier };
    }
}
