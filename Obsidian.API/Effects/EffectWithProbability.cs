namespace Obsidian.API.Effects;
/// <summary>A consume effect that gives the consumer a potion effect, with a chance that it applies.</summary>
public sealed record class EffectWithProbability : IConsumeEffect
{
    /// <inheritdoc/>
    public string Type => "minecraf:apply_effects";

    /// <summary>Gets the potion effect to give.</summary>
    public PotionEffectData EffectData { get; init; }

    /// <summary>Gets the chance, from 0 to 1, that the effect is given.</summary>
    public float Probability { get; init; }

    /// <inheritdoc/>
    public void Write(INetStreamWriter writer)
    {
        PotionEffectData.Write(this.EffectData, writer);
        writer.WriteSingle(this.Probability);
    }

    /// <inheritdoc/>
    public IConsumeEffect Read(INetStreamReader reader) => new EffectWithProbability
    {
        EffectData = reader.ReadPotionEffectData(),
        Probability = reader.ReadSingle()
    };
}
