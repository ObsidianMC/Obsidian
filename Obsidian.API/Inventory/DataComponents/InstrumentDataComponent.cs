using Obsidian.API.Registries;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Instrument value, resource key, or holder ID in the current connection's registry.</summary>
public sealed record InstrumentDataComponent() : SimpleDataComponent<InstrumentData>(
    DataComponentType.Instrument, "minecraft:instrument", (_, _) => { }, _ => new())
{
    public int? RegistryId { get; set; }
    public bool IsResourceKey { get; set; }
    public override void Read(INetStreamReader reader)
    {
        this.IsResourceKey = !reader.ReadBoolean();
        this.Value = new();
        if (this.IsResourceKey) { this.Value.Identifier = reader.ReadString(); return; }
        var holder = reader.ReadVarInt();
        this.RegistryId = holder == 0 ? null : holder - 1;
        if (holder != 0) return;
        this.Value.SoundEvent = ComponentValueCodecs.ReadSoundHolder(reader);
        this.Value.UseDuration = reader.ReadSingle(); this.Value.Range = reader.ReadSingle(); this.Value.Description = reader.ReadChat();
    }
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteBoolean(!this.IsResourceKey);
        if (this.IsResourceKey) { writer.WriteString(this.Value.Identifier!); return; }
        var id = this.RegistryId ?? InstrumentsRegistry.All.FirstOrDefault(instrument => instrument.Identifier == this.Value.Identifier)?.Id;
        writer.WriteVarInt(id is int value ? value + 1 : 0);
        if (id is not null) return;
        ComponentValueCodecs.WriteSoundHolder(this.Value.SoundEvent, writer);
        writer.WriteSingle(this.Value.UseDuration); writer.WriteSingle(this.Value.Range); writer.WriteChat(this.Value.Description);
    }
}
