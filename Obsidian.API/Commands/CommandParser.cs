namespace Obsidian.API.Commands;
public abstract class CommandParser
{
    public abstract int Id { get; }
    public abstract string Identifier { get; }

    public virtual void Write(INetStreamWriter writer) => writer.WriteVarInt(this.Id);

    public override string ToString() => Identifier;
}
