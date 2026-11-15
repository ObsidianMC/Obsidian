using Obsidian.API.Effects;
using System.Diagnostics.CodeAnalysis;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>
/// How an item is consumed (<c>minecraft:consumable</c>): how long it takes, its animation and sound, and the effects
/// it gives once consumed.
/// </summary>
public sealed record class ConsumableDataComponent : DataComponent
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.consumeSeconds,
        this.animation,
        this.sound,
        this.hasConsumeParticles,
        ComponentValueKey.Sequence(this.effects)
    });

    /// <inheritdoc/>
    public bool Equals(ConsumableDataComponent? other) => ReferenceEquals(this, other)
        || other is not null && base.Equals(other) && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        HashCode.Combine(base.GetHashCode(), this.Comparison);

    /// <inheritdoc/>
    public override DataComponentType Type => DataComponentType.Consumable;

    /// <inheritdoc/>
    public override string Identifier => "minecraft:consumable";

    private float consumeSeconds;

    /// <summary>
    /// Gets how long consuming the item takes, in seconds.
    /// </summary>
    public required float ConsumeSeconds
    {
        get => this.consumeSeconds;
        init => this.consumeSeconds = value;
    }

    private ItemAnimation animation;

    /// <summary>
    /// Gets the animation the consumer plays.
    /// </summary>
    public required ItemAnimation Animation
    {
        get => this.animation;
        init => this.animation = value;
    }

    private SoundEvent sound;

    /// <summary>
    /// Gets the sound played while the item is consumed.
    /// </summary>
    public required SoundEvent Sound
    {
        get => this.sound;
        init => this.sound = value;
    }

    private bool hasConsumeParticles;

    /// <summary>
    /// Gets whether particles of the item show while it's consumed.
    /// </summary>
    public bool HasConsumeParticles
    {
        get => this.hasConsumeParticles;
        init => this.hasConsumeParticles = value;
    }

    private ImmutableArray<ConsumeEffect> effects = [];

    /// <summary>
    /// Gets the effects applied once the item is consumed.
    /// </summary>
    public ImmutableArray<ConsumeEffect> Effects
    {
        get => this.effects;
        init => this.effects = value;
    }

    [SetsRequiredMembers]
    internal ConsumableDataComponent() { }

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public override void Write(INetStreamWriter writer)
    {
        writer.WriteSingle(this.ConsumeSeconds);
        writer.WriteVarInt(this.Animation);
        ComponentValueCodecs.WriteSoundHolder(this.Sound, writer);
        writer.WriteBoolean(this.HasConsumeParticles);
        writer.WriteLengthPrefixedArray(effect => ComponentConsumeEffect.WriteValue(effect.Effect, writer), this.Effects.ToArray());
    }
}
