using Obsidian.API.Effects;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>The five 26.3 consume-effect payloads, selected by their registry ID.</summary>
public sealed record ComponentConsumeEffect : IConsumeEffect
{
    private static readonly string[] Names = ["apply_effects", "remove_effects", "clear_all_effects", "teleport_randomly", "play_sound"];
    public int Id { get; init; }
    public string Type => "minecraft:" + Names[this.Id];
    private ImmutableArray<PotionEffectData> effects = [];

    public ImmutableArray<PotionEffectData> Effects
    {
        get => this.effects;
        init => this.effects = value;
    }

    private float probability = 1;

    public float Probability
    {
        get => this.probability;
        init => this.probability = value;
    }

    private IdSet removedEffects;

    public IdSet RemovedEffects
    {
        get => this.removedEffects;
        init => this.removedEffects = value;
    }

    private float diameter = 16;

    public float Diameter
    {
        get => this.diameter;
        init => this.diameter = value;
    }

    private bool directionalParticles = true;

    public bool DirectionalParticles
    {
        get => this.directionalParticles;
        init => this.directionalParticles = value;
    }

    private SoundEvent sound;

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
            effect.Write(writer);
    }

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
