namespace Obsidian.API.Effects;
public sealed record class EffectWithProbability : IConsumeEffect
{
    public string Type => "minecraf:apply_effects";

    public PotionEffectData EffectData { get; init; }

    public float Probability { get; init; }

    public void Write(INetStreamWriter writer)
    {
        PotionEffectData.Write(this.EffectData, writer);
        writer.WriteSingle(this.Probability);
    }

    public IConsumeEffect Read(INetStreamReader reader) => new EffectWithProbability
    {
        EffectData = reader.ReadPotionEffectData(),
        Probability = reader.ReadSingle()
    };
}
