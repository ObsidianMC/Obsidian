namespace Obsidian.API.Commands;
public abstract class CommandParser
{
    public abstract int Id { get; }
    public abstract string Identifier { get; }

    public virtual void Write(INetStreamWriter writer) => writer.WriteVarInt(this.Id);

    public override string ToString() => Identifier;
}

public sealed class EmptyCommandParser(int id, string identifier) : CommandParser
{
    public override int Id { get; } = id;
    public override string Identifier { get; } = identifier;

}
