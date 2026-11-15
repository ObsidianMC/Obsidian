namespace Obsidian.API.Inventory.DataComponents;

/// <summary>Inline instrument value or holder ID in the current connection's registry.</summary>
public sealed record InstrumentDataComponent() : SimpleDataComponent<InstrumentData>(
    DataComponentType.Instrument, "minecraft:instrument", (_, _) => { }, _ => new())
{
    private int? registryId;

    /// <summary>
    /// Gets the instrument's id in the server's instrument registry, translated from and to each connection's ids when
    /// the component is read or written. When null, writing takes the id of
    /// <see cref="SimpleDataComponent{TValue}.Value"/>'s identifier, or sends the instrument inline when it has none.
    /// </summary>
    public int? RegistryId
    {
        get => this.registryId;
        init => this.registryId = value;
    }

    /// <inheritdoc/>
    protected override void ReadCore(INetStreamReader reader)
    {
        this.registryId = null;
        this.value = new();
        this.registryId = reader.ReadRegistryHolder("minecraft:instrument");
        if (this.registryId is not null)
            return;

        this.value = new()
        {
            SoundEvent = ComponentValueCodecs.ReadSoundHolder(reader),
            UseDuration = reader.ReadSingle(),
            Range = reader.ReadSingle(),
            DurabilityDamage = reader.ReadVarInt(),
            Description = reader.ReadChat()
        };
    }

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        var id = this.RegistryId ?? (this.Value.Identifier is string name
            ? ComponentRegistryNames.Id("minecraft:instrument", name) : (int?)null);
        writer.WriteRegistryHolder("minecraft:instrument", id);
        if (id is not null)
            return;

        ComponentValueCodecs.WriteSoundHolder(this.Value.SoundEvent, writer);
        writer.WriteSingle(this.Value.UseDuration);
        writer.WriteSingle(this.Value.Range);
        writer.WriteVarInt(this.Value.DurabilityDamage);
        writer.WriteChat(this.Value.Description);
    }
}
