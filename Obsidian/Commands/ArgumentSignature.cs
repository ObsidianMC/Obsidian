namespace Obsidian.Commands;
public readonly struct ArgumentSignature
{
    public string ArgumentName { get; init; }

    public int SignatureLength => this.Signature.Length;

    public byte[] Signature { get; init; }
}
