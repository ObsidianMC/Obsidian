using Obsidian.API.Effects;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;
public sealed record class ConsumableDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Consumable;

    public override string Identifier => "minecraft:consumable";

    public required float ConsumeSeconds { get; set; }

    public required ItemAnimation Animation { get; set; }

    public required SoundEvent Sound { get; set; }

    public bool HasConsumeParticles { get; set; }

    public List<ConsumeEffect> Effects { get; set; } = [];

    [SetsRequiredMembers]
    internal ConsumableDataComponent() { }

    public override void Read(INetStreamReader reader)
    {
        this.ConsumeSeconds = reader.ReadSingle();
        this.Animation = reader.ReadVarInt<ItemAnimation>();
        this.Sound = ComponentValueCodecs.ReadSoundHolder(reader);
        this.HasConsumeParticles = reader.ReadBoolean();
        this.Effects = reader.ReadLengthPrefixedArray(() =>
        {
            var effect = ComponentConsumeEffect.ReadValue(reader);
            return new ConsumeEffect { Type = effect.Type, Effect = effect };
        }).ToList();
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteSingle(this.ConsumeSeconds);
        writer.WriteVarInt(this.Animation);
        ComponentValueCodecs.WriteSoundHolder(this.Sound, writer);
        writer.WriteBoolean(this.HasConsumeParticles);
        writer.WriteLengthPrefixedArray(effect => ComponentConsumeEffect.WriteValue(effect.Effect, writer), this.Effects.ToArray());
    }
}
