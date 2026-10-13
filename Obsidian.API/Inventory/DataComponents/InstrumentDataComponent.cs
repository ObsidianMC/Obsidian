using Obsidian.API.Registries;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Instrument value, resource key, or holder ID in the current connection's registry.</summary>
public sealed record InstrumentDataComponent() : SimpleDataComponent<InstrumentData>(
    DataComponentType.Instrument, "minecraft:instrument", (_, _) => { }, _ => new())
{
    private int? registryId;

    public int? RegistryId
    {
        get => this.registryId;
        init => this.registryId = value;
    }

    private bool isResourceKey;

    public bool IsResourceKey
    {
        get => this.isResourceKey;
        init => this.isResourceKey = value;
    }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.registryId = null;
        this.isResourceKey = !reader.ReadBoolean();
        this.value = new();
        if (this.IsResourceKey)
        {
            this.value = new() { Identifier = reader.ReadString() };
            return;
        }

        var holder = reader.ReadVarInt();
        this.registryId = holder == 0 ? null : holder - 1;
        if (holder != 0)
            return;

        this.value = new()
        {
            SoundEvent = ComponentValueCodecs.ReadSoundHolder(reader),
            UseDuration = reader.ReadSingle(),
            Range = reader.ReadSingle(),
            Description = reader.ReadChat()
        };
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteBoolean(!this.IsResourceKey);
        if (this.IsResourceKey)
        {
            writer.WriteString(this.Value.Identifier!);
            return;
        }

        var id = this.RegistryId ?? InstrumentsRegistry.All.FirstOrDefault(instrument => instrument.Identifier == this.Value.Identifier)?.Id;
        writer.WriteVarInt(id is int value ? value + 1 : 0);
        if (id is not null)
            return;

        ComponentValueCodecs.WriteSoundHolder(this.Value.SoundEvent, writer);
        writer.WriteSingle(this.Value.UseDuration);
        writer.WriteSingle(this.Value.Range);
        writer.WriteChat(this.Value.Description);
    }
}
