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
        this.Sound = reader.ReadSoundEvent();

        var count = reader.ReadVarInt();
        var effects = new List<ConsumeEffect>(count);

        for (int i = 0; i < count; i++)
        {
            var type = reader.ReadString();

            var effect = ConsumeEffects.Compile(type);

            effects[i] = new() { Effect = effect, Type = type };
        }
    }

    public override void Write(INetStreamWriter writer)
    {
        writer.WriteSingle(this.ConsumeSeconds);
        writer.WriteVarInt(this.Animation);
        writer.WriteSoundEvent(this.Sound);
        writer.WriteBoolean(this.HasConsumeParticles);

        foreach (var consumeEffect in this.Effects)
        {
            writer.WriteString(consumeEffect.Type);
            consumeEffect.Effect.Write(writer);
        }
    }
}
