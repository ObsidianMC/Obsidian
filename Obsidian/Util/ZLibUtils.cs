using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System.IO;

namespace Obsidian.Util
{
    public static class ZLibUtils
    {
        //TODO: Make this async >:( - Craftplacer
        public static byte[] Compress(byte[] to_compress)
        {
            byte[] data;
            using var memstream = new MemoryStream();

            using var stream = new ZlibStream(memstream, CompressionMode.Compress, CompressionLevel.BestCompression);

            stream.Write(to_compress, 0, to_compress.Length);

            data = memstream.ToArray();

            return data;
        }

        /// <summary>
        /// Decompress a byte array into another byte array of the specified size
        /// </summary>
        /// <param name="to_decompress">Data to decompress</param>
        /// <param name="size_uncompressed">Size of the data once decompressed</param>
        /// <returns>Decompressed data as a byte array</returns>
        public static byte[] Decompress(byte[] to_decompress, int size_uncompressed)
        {
            var stream = new ZlibStream(new MemoryStream(to_decompress, false), CompressionMode.Decompress, CompressionLevel.BestSpeed);
            byte[] packetData_decompressed = new byte[size_uncompressed];
            stream.Read(packetData_decompressed, 0, size_uncompressed);
            stream.Close();
            return packetData_decompressed;
        }

        /// <summary>
        /// Decompress a byte array into another byte array of a potentially unlimited size (!)
        /// </summary>
        /// <param name="to_decompress">Data to decompress</param>
        /// <returns>Decompressed data as byte array</returns>
        public static byte[] Decompress(byte[] to_decompress)
        {
            var stream = new ZlibStream(new MemoryStream(to_decompress, false), CompressionMode.Decompress, CompressionLevel.BestSpeed);
            byte[] buffer = new byte[16 * 1024];
            using var decompressedBuffer = new MemoryStream();
            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                decompressedBuffer.Write(buffer, 0, read);
            return decompressedBuffer.ToArray();
        }
    }
}
