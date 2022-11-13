namespace Obsidian.API;
public readonly struct SignedMessage
{
    public required Guid UserId { get; init; }

    public required byte[] Signature { get; init; }
}
