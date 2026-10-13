namespace Obsidian.API.Effects;
public interface IConsumeEffect
{
    public string Type { get; }

    public void Write(INetStreamWriter writer);
    /// <summary>Reads a new effect without changing this value.</summary>
    public IConsumeEffect Read(INetStreamReader reader);
}
