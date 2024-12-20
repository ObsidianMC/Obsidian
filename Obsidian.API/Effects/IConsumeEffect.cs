namespace Obsidian.API.Effects;
public interface IConsumeEffect
{
    public string Type { get; }

    public void Write(INetStreamWriter writer);
    public void Read(INetStreamReader reader);
}
