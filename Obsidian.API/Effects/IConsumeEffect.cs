namespace Obsidian.API.Effects;
/// <summary>An immutable consume effect shared by item components.</summary>
/// <remarks>
/// Implementations, including plugin effects, must keep all reachable state immutable after construction.
/// Read must return a new value; Write must not mutate this effect or retain mutable writer arguments.
/// </remarks>
public interface IConsumeEffect
{
    public string Type { get; }

    public void Write(INetStreamWriter writer);
    /// <summary>Reads a new effect without changing this value.</summary>
    public IConsumeEffect Read(INetStreamReader reader);
}
