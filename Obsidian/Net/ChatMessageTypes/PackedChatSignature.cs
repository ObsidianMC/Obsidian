namespace Obsidian.Net.ChatMessageTypes;

/// <summary>A cached signature index, or a complete 256-byte signature when Index is -1.</summary>
public readonly record struct PackedChatSignature(int Index, ReadOnlyMemory<byte> Signature)
{
    internal void Write(INetStreamWriter writer)
    {
        if (this.Index < -1 || this.Index == -1 && this.Signature.Length != 256)
            throw new ArgumentException("A full chat signature must contain 256 bytes.");
        writer.WriteVarInt(this.Index + 1);
        if (this.Index == -1)
            writer.WriteByteArray(this.Signature.Span);
    }
}
