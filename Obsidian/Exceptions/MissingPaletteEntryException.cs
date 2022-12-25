namespace Obsidian.Exceptions;
public sealed class MissingPaletteEntryException : Exception
{
    public MissingPaletteEntryException(int index) : base($"Missing entry at index {index}") { }
}
