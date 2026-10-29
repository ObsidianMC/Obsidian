using Obsidian.API.Registries;

namespace Obsidian.API;
public sealed record class InstrumentData : INetworkSerializable<InstrumentData>
{
    /// <summary>
    /// The registry id of a vanilla instrument (e.g. <c>minecraft:ponder_goat_horn</c>), or null for a custom one.
    /// Vanilla instruments are sent by registry id; custom ones are sent with their data.
    /// </summary>
    public string? Identifier { get; init; }

    public SoundEvent SoundEvent { get; init; }

    public float UseDuration { get; init; }

    public float Range { get; init; }

    public int DurabilityDamage { get; init; }

    public ChatMessage Description
    {
        get => Inventory.DataComponents.ComponentCopies.Chat(field)!;
        init => field = Inventory.DataComponents.ComponentCopies.Chat(value)!;
    }

    // Instrument holders use registry ID + 1, or zero followed by the inline definition.
    public static InstrumentData Read(INetStreamReader reader)
    {
        var holder = reader.ReadVarInt();
        if (holder > 0)
            return FromRegistry(InstrumentsRegistry.All[holder - 1].Identifier);

        // Sound events are holders too: 0 means the sound event follows, else it's the registry id + 1.
        var soundHolder = reader.ReadVarInt();
        return new()
        {
            SoundEvent = soundHolder == 0
                ? reader.ReadSoundEvent()
                : new SoundEvent { ResourceLocation = string.Empty, RegistryId = soundHolder - 1 },
            UseDuration = reader.ReadSingle(),
            Range = reader.ReadSingle(),
            DurabilityDamage = reader.ReadVarInt(),
            Description = reader.ReadChat()
        };
    }

    public static void Write(InstrumentData value, INetStreamWriter writer)
    {
        var registered = InstrumentsRegistry.All.FirstOrDefault(instrument => instrument.Identifier == value.Identifier);
        if (registered is not null)
        {
            writer.WriteVarInt(registered.Id + 1);
            return;
        }

        writer.WriteVarInt(0);
        if (value.SoundEvent.RegistryId is not null)
        {
            writer.WriteVarInt(value.SoundEvent.RegistryId.Value + 1);
        }
        else
        {
            writer.WriteVarInt(0);
            writer.WriteSoundEvent(value.SoundEvent);
        }

        writer.WriteSingle(value.UseDuration);
        writer.WriteSingle(value.Range);
        writer.WriteVarInt(value.DurabilityDamage);
        writer.WriteChat(value.Description);
    }

    private static InstrumentData FromRegistry(string identifier)
    {
        var instrument = InstrumentsRegistry.All.FirstOrDefault(instrument => instrument.Identifier == identifier);
        return instrument is not null ? instrument.ToInstrumentData() : new() { Identifier = identifier };
    }
}
