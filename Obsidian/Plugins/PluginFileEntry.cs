using System.IO;
using System.IO.Compression;

namespace Obsidian.Plugins;
public sealed class PluginFileEntry
{
    internal byte[] rawData = default!;

    public required string Name { get; init; }

    public required int Length { get; init; }

    public required int CompressedLength { get; init; }

    public required int Offset { get; set; }

    public bool IsCompressed => Length != CompressedLength;

    internal byte[] GetData()
    {
        if (!this.IsCompressed)
            return this.rawData;

        using var ms = new MemoryStream(this.rawData, false);
        using var ds = new DeflateStream(ms, CompressionMode.Decompress);
        using var outStream = new MemoryStream();

        ds.CopyTo(outStream);

        return outStream.Length != this.Length ? throw new Exception("Invalid stream length") : outStream.ToArray();
    }
}
