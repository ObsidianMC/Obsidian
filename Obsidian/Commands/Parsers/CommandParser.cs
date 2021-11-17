using Obsidian.Net;

namespace Obsidian.Commands;

public class CommandParser
{
    private string Identifier { get; }

    public CommandParser(string identifier) => Identifier = identifier ?? throw new ArgumentNullException(nameof(identifier));

    public virtual Task WriteAsync(MinecraftStream stream) => stream.WriteStringAsync(Identifier);
    public virtual void Write(MinecraftStream stream) => stream.WriteString(Identifier);

    public override string ToString() => Identifier;
}
