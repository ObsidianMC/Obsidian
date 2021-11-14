using System;

namespace Obsidian.Commands;

[Obsolete("Note: this should only be used as a workaround until specific code has been written for the parser")]
public class EmptyFlagsCommandParser : CommandParser
{
    public EmptyFlagsCommandParser(string identifier) : base(identifier)
    {
    }
}
