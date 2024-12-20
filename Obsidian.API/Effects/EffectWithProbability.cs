namespace Obsidian.API.Effects;
public sealed record class EffectWithProbability : IConsumeEffect
{
    public string Type => "minecraf:apply_effects";

    public PotionEffectData EffectData { get; set; }

    public float Probability { get; set; }

    public void Write(INetStreamWriter writer)
    {
        PotionEffectData.Write(this.EffectData, writer);
        writer.WriteFloat(this.Probability);
    }

    public void Read(INetStreamReader reader)
    {
        this.EffectData = reader.ReadPotionEffectData();
        this.Probability = reader.ReadFloat();
    }
}
