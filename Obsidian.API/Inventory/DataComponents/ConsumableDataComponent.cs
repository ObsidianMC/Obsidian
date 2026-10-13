using Obsidian.API.Effects;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

public sealed record class ConsumableDataComponent : DataComponent
{
    public override DataComponentType Type => DataComponentType.Consumable;

    public override string Identifier => "minecraft:consumable";

    private float consumeSeconds;

    public required float ConsumeSeconds
    {
        get => this.consumeSeconds;
        init => this.consumeSeconds = value;
    }

    private ItemAnimation animation;

    public required ItemAnimation Animation
    {
        get => this.animation;
        init => this.animation = value;
    }

    private SoundEvent sound;

    public required SoundEvent Sound
    {
        get => this.sound;
        init => this.sound = value;
    }

    private bool hasConsumeParticles;

    public bool HasConsumeParticles
    {
        get => this.hasConsumeParticles;
        init => this.hasConsumeParticles = value;
    }

    private ImmutableArray<ConsumeEffect> effects = [];

    public ImmutableArray<ConsumeEffect> Effects
    {
        get => this.effects;
        init => this.effects = value;
    }

    [SetsRequiredMembers]
    internal ConsumableDataComponent() { }

    protected override void ReadCore(INetStreamReader reader)
    {
        this.consumeSeconds = reader.ReadSingle();
        this.animation = reader.ReadVarInt<ItemAnimation>();
        this.sound = ComponentValueCodecs.ReadSoundHolder(reader);
        this.hasConsumeParticles = reader.ReadBoolean();
        this.effects = reader.ReadLengthPrefixedArray(() =>
        {
            var effect = ComponentConsumeEffect.ReadValue(reader);
            return new ConsumeEffect { Type = effect.Type, Effect = effect };
        }).ToImmutableArray();
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
