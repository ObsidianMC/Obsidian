using Obsidian.API.Effects;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>The five 26.3 consume-effect payloads, selected by their registry ID.</summary>
public sealed record ComponentConsumeEffect : IConsumeEffect
{
    private ComponentValueKey Comparison => new(new object?[]
    {
        this.Id,
        ComponentValueKey.Sequence(this.effects),
        this.probability,
        this.removedEffects,
        this.diameter,
        this.directionalParticles,
        this.sound
    });

    /// <inheritdoc/>
    public bool Equals(ComponentConsumeEffect? other) => ReferenceEquals(this, other)
        || other is not null && this.Comparison.Equals(other.Comparison);

    /// <inheritdoc/>
    public override int GetHashCode() =>
        this.Comparison.GetHashCode();

    private static readonly string[] Names = ["apply_effects", "remove_effects", "clear_all_effects", "teleport_randomly", "play_sound"];
    /// <summary>
    /// Gets the effect's kind: 0 applies <see cref="Effects"/>, 1 removes <see cref="RemovedEffects"/>, 2 clears all
    /// effects, 3 teleports the consumer randomly within <see cref="Diameter"/> and 4 plays <see cref="Sound"/>.
    /// </summary>
    public int Id { get; init; }
    /// <summary>
    /// Gets the effect's type id, like <c>minecraft:apply_effects</c>, from <see cref="Id"/>.
    /// </summary>
    public string Type => "minecraft:" + Names[this.Id];
    private ImmutableArray<PotionEffectData> effects = [];

    /// <summary>
    /// Gets the effects applied, for <c>minecraft:apply_effects</c>.
    /// </summary>
    public ImmutableArray<PotionEffectData> Effects
    {
        get => this.effects;
        init => this.effects = value;
    }

    private float probability = 1;

    /// <summary>
    /// Gets the chance the effects are applied, from 0 to 1, for <c>minecraft:apply_effects</c>.
    /// </summary>
    public float Probability
    {
        get => this.probability;
        init => this.probability = value;
    }

    private IdSet removedEffects;

    /// <summary>
    /// Gets the effects removed, for <c>minecraft:remove_effects</c>.
    /// </summary>
    public IdSet RemovedEffects
    {
        get => this.removedEffects;
        init => this.removedEffects = value;
    }

    private float diameter = 16;

    /// <summary>
    /// Gets the size of the area the consumer may be teleported in, in blocks, for <c>minecraft:teleport_randomly</c>.
    /// </summary>
    public float Diameter
    {
        get => this.diameter;
        init => this.diameter = value;
    }

    private bool directionalParticles = true;

    /// <summary>
    /// Gets whether teleport particles show the direction moved, for <c>minecraft:teleport_randomly</c>.
    /// </summary>
    public bool DirectionalParticles
    {
        get => this.directionalParticles;
        init => this.directionalParticles = value;
    }

    private SoundEvent sound;

    /// <summary>
    /// Gets the sound played, for <c>minecraft:play_sound</c>.
    /// </summary>
    public SoundEvent Sound
    {
        get => this.sound;
        init => this.sound = value;
    }

    internal static ComponentConsumeEffect ReadValue(INetStreamReader reader)
    {
        var value = new ComponentConsumeEffect { Id = reader.ReadVarInt() };
        return (ComponentConsumeEffect)value.Read(reader);
    }

    internal static void WriteValue(IConsumeEffect effect, INetStreamWriter writer)
    {
        var id = Array.IndexOf(Names, effect.Type.Replace("minecraft:", "").Replace("minecraf:", ""));
        if (id < 0)
            throw new ArgumentException("Unknown consume effect.", nameof(effect));

        writer.WriteVarInt(id);
        if (effect is EffectWithProbability legacy)
        {
            writer.WriteVarInt(1);
            ComponentValueCodecs.WriteEffect(legacy.EffectData, writer);
            writer.WriteSingle(legacy.Probability);
        }
        else
        {
            effect.Write(writer);
        }
    }

    /// <inheritdoc/>
    public IConsumeEffect Read(INetStreamReader reader)
    {
        var result = this with { };
        result.ReadCore(reader);
        return result;
    }

    private void ReadCore(INetStreamReader reader)
    {
        switch (this.Id)
        {
            case 0:
                this.effects = reader.ReadLengthPrefixedArray(() => ComponentValueCodecs.ReadEffect(reader))
                    .ToImmutableArray();
                this.probability = reader.ReadSingle();
                break;
            case 1:
                this.removedEffects = reader.ReadIdSet(ComponentValueCodecs.MobEffects);
                break;
            case 2:
                break;
            case 3:
                this.diameter = reader.ReadSingle();
                this.directionalParticles = reader.ReadBoolean();
                break;
            case 4:
                this.sound = ComponentValueCodecs.ReadSoundHolder(reader);
                break;
            default:
                throw new System.IO.InvalidDataException("Unknown consume effect.");
        }
    }

    /// <summary>
    /// Writes the effect's payload as clients read it, without its <see cref="Id"/>.
    /// </summary>
    /// <exception cref="System.IO.InvalidDataException"><see cref="Id"/> isn't from 0 to 4.</exception>
    public void Write(INetStreamWriter writer)
    {
        switch (this.Id)
        {
            case 0:
                writer.WriteLengthPrefixedArray(
                    value => ComponentValueCodecs.WriteEffect(value, writer), this.Effects.AsSpan());
                writer.WriteSingle(this.Probability);
                break;
            case 1:
                IdSet.Write(this.RemovedEffects, writer, ComponentValueCodecs.MobEffects);
                break;
            case 2:
                break;
            case 3:
                writer.WriteSingle(this.Diameter);
                writer.WriteBoolean(this.DirectionalParticles);
                break;
            case 4:
                ComponentValueCodecs.WriteSoundHolder(this.Sound, writer);
                break;
            default:
                throw new System.IO.InvalidDataException("Unknown consume effect.");
        }
    }
}
