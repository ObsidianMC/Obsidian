using Obsidian.API.Effects;

namespace Obsidian.API.Inventory.DataComponents;

/// <summary>The five 1.21.11 consume-effect payloads, selected by their registry ID.</summary>
public sealed class ComponentConsumeEffect : IConsumeEffect
{
    private static readonly string[] Names = ["apply_effects", "remove_effects", "clear_all_effects", "teleport_randomly", "play_sound"];
    public int Id { get; init; }
    public string Type => "minecraft:" + Names[this.Id];
    public PotionEffectData[] Effects { get; set; } = [];
    public float Probability { get; set; } = 1;
    public IdSet RemovedEffects { get; set; }
    public float Diameter { get; set; } = 16;
    public SoundEvent Sound { get; set; }

    internal static ComponentConsumeEffect ReadValue(INetStreamReader reader)
    {
        var value = new ComponentConsumeEffect { Id = reader.ReadVarInt() };
        value.Read(reader); return value;
    }
    internal static void WriteValue(IConsumeEffect effect, INetStreamWriter writer)
    {
        var id = Array.IndexOf(Names, effect.Type.Replace("minecraft:", "").Replace("minecraf:", ""));
        if (id < 0) throw new ArgumentException("Unknown consume effect.", nameof(effect));
        writer.WriteVarInt(id);
        if (effect is EffectWithProbability legacy)
        {
            writer.WriteVarInt(1); ComponentValueCodecs.WriteEffect(legacy.EffectData, writer); writer.WriteSingle(legacy.Probability);
        }
        else effect.Write(writer);
    }
    public void Read(INetStreamReader reader)
    {
        switch (this.Id)
        {
            case 0: this.Effects = reader.ReadLengthPrefixedArray(() => ComponentValueCodecs.ReadEffect(reader)); this.Probability = reader.ReadSingle(); break;
            case 1: this.RemovedEffects = reader.ReadIdSet(); break;
            case 2: break;
            case 3: this.Diameter = reader.ReadSingle(); break;
            case 4: this.Sound = ComponentValueCodecs.ReadSoundHolder(reader); break;
            default: throw new System.IO.InvalidDataException("Unknown consume effect.");
        }
    }
    public void Write(INetStreamWriter writer)
    {
        switch (this.Id)
        {
            case 0: writer.WriteLengthPrefixedArray(value => ComponentValueCodecs.WriteEffect(value, writer), this.Effects); writer.WriteSingle(this.Probability); break;
            case 1: IdSet.Write(this.RemovedEffects, writer); break;
            case 2: break;
            case 3: writer.WriteSingle(this.Diameter); break;
            case 4: ComponentValueCodecs.WriteSoundHolder(this.Sound, writer); break;
            default: throw new System.IO.InvalidDataException("Unknown consume effect.");
        }
    }
}
