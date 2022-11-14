namespace Obsidian.Net.ChatMessageTypes;

[Flags]
public enum ChatFilterType : int
{
    PassThrough,
    FullyFiltered,
    PartiallyFiltered
}
