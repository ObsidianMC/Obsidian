namespace Obsidian.API;
public readonly struct ArgumentSignature
{
    public required string ArgumentName { get; init; }

    public int SignatureLength => this.Signature.Length;

    public required byte[] Signature { get; init; }
}
