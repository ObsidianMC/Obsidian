namespace Obsidian.Net;

/// <summary>
/// Frames a connection's serialized packets for its socket, in the order they were sent (see
/// <see cref="NetworkBuffer.WriteFrame"/>), and encrypts the stream from the frame that turns encryption on. Used by
/// one thread at a time.
/// </summary>
/// <param name="compressionLevel">
/// The zlib level compressed frames are written at (see <see cref="NetworkBuffer.WriteFrame"/>).
/// </param>
internal sealed class PacketFramer(int compressionLevel = NetworkBuffer.DefaultCompressionLevel)
{
    private AesCfbBlockCipher? encryptor;

    /// <summary>
    /// Appends <paramref name="frame"/> to <paramref name="output"/>, or turns encryption on for the frames after it.
    /// </summary>
    public void Write(NetworkBuffer output, OutgoingFrame frame)
    {
        if (frame.EncryptionKey is byte[] key)
        {
            this.encryptor = new AesCfbBlockCipher(key);
            return;
        }

        var start = output.Offset;
        output.WriteFrame(frame.Body!.AsSpan(0, frame.Body.Size), frame.CompressionThreshold, compressionLevel);
        this.encryptor?.EncryptInPlace(output.AsSpan(start, output.Offset - start));
    }
}

/// <summary>
/// A packet serialized for a connection (see <see cref="NetworkBuffer.SerializeBodies"/>), framed later with the
/// compression threshold it was sent under, or, with an <see cref="EncryptionKey"/>, the point the stream's encryption
/// starts.
/// </summary>
internal readonly record struct OutgoingFrame(NetworkBuffer? Body, int CompressionThreshold, byte[]? EncryptionKey = null);
