namespace Obsidian.Commands;

public class CommandParser
{
    public int Id { get; }
    private string Identifier { get; }

    public CommandParser(int id, string identifier)
    {
        this.Id = id;
        this.Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));
    }

    public virtual void Write(INetStreamWriter writer) => writer.WriteVarInt(this.Id);

    public override string ToString() => Identifier;
}
