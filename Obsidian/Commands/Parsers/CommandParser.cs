namespace Obsidian.Commands;

public class CommandParser(int id, string identifier)
{
    public int Id { get; } = id;
    private string Identifier { get; } = identifier ?? throw new ArgumentNullException(nameof(identifier));

    public virtual void Write(INetStreamWriter writer) => writer.WriteVarInt(this.Id);

    public override string ToString() => Identifier;
}
