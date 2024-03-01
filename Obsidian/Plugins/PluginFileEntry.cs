using Org.BouncyCastle.Crypto;
using System.Buffers;
using System.IO;
using System.IO.Compression;

namespace Obsidian.Plugins;
public sealed class PluginFileEntry
{
    public required string FullName { get; init; }

    public required int Length { get; init; }

    public required int CompressedLength { get; init; }

    public required int Offset { get; set; }

    public bool IsCompressed => CompressedLength < Length;

    public async Task<byte[]> GetDataAsync(FileStream packedPluginFile)
    {
        packedPluginFile.Seek(this.Offset, SeekOrigin.Begin);

        if (!this.IsCompressed)
        {
            var mem = new byte[this.Length];

            return await packedPluginFile.ReadAsync(mem) != this.Length ? throw new DataLengthException() : mem;
        }

        var compressedData = ArrayPool<byte>.Shared.Rent(this.CompressedLength);

        if (await packedPluginFile.ReadAsync(compressedData.AsMemory(0, this.CompressedLength)) != this.CompressedLength)
            throw new DataLengthException();

        await using var ms = new MemoryStream(compressedData);
        await using var ds = new DeflateStream(ms, CompressionMode.Decompress);
        await using var outStream = new MemoryStream();

        await ds.CopyToAsync(outStream);

        ArrayPool<byte>.Shared.Return(compressedData);

        return outStream.ToArray();
    }
}
