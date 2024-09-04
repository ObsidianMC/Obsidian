namespace Obsidian.Nbt.Exceptions;
public sealed class TagNotFoundException : Exception
{
    public TagNotFoundException(string? message) : base(message)
    {
    }

    public TagNotFoundException(string? message, Exception? innerException) : base(message, innerException) { }
}
