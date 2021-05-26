using SharpCompress.Compressors;
using SharpCompress.Compressors.Deflate;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Utilities
{
    public static class ZLibUtils
    {
        public static async Task CompressAsync(byte[] byteIn, Stream outStream)
        {
            using var stream = new ZlibStream(new MemoryStream(), CompressionMode.Compress, CompressionLevel.BestCompression);

            await stream.WriteAsync(byteIn, 0, byteIn.Length);

            await stream.CopyToAsync(outStream);
        }

        public static async Task CompressAsync(Stream inputStream, Stream outputStream)
        {
            await using var stream = new ZlibStream(inputStream, CompressionMode.Compress, CompressionLevel.BestCompression);
            await stream.CopyToAsync(outputStream);
        }

        public static async Task<byte[]> DecompressAsync(byte[] byteIn, int size)
        {
            using var stream = new ZlibStream(new MemoryStream(byteIn, false), CompressionMode.Decompress, CompressionLevel.BestSpeed);

            var data = new byte[size];

            await stream.ReadAsync(byteIn, 0, size);

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
            using var stream = new ZlibStream(new MemoryStream(to_decompress, false), CompressionMode.Decompress, CompressionLevel.BestSpeed);
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
            using var stream = new ZlibStream(new MemoryStream(to_decompress, false), CompressionMode.Decompress, CompressionLevel.BestSpeed);

            byte[] buffer = new byte[16 * 1024];

            using var decompressedBuffer = new MemoryStream();

            int read;
            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                decompressedBuffer.Write(buffer, 0, read);

            return decompressedBuffer.ToArray();
        }
    }
}
