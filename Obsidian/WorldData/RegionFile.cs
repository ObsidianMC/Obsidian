using Obsidian.API;
using System;
using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class RegionFile : IDisposable
    {
        private readonly string filePath;
        private readonly int cubicRegionSize;
        private readonly int indexSize;
        private FileStream regionFileStream;
        private RegionFileHeaderTable locationTable, timestampTable;
        private bool disposedValue;
        private readonly IMemoryOwner<byte> fileCache;

        public RegionFile(string filePath, int cubicRegionSize = 32) 
        {
            this.filePath = filePath;
            this.cubicRegionSize = cubicRegionSize;
            this.indexSize = cubicRegionSize * 4;

            fileCache = MemoryPool<byte>.Shared.Rent();
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(filePath))
            {
                await InitializeNewFileAsync();
            }

            regionFileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);

            // Load file into memory
            await regionFileStream.ReadAsync(fileCache.Memory);
            regionFileStream.Seek(0, SeekOrigin.Begin);

            locationTable = new RegionFileHeaderTable(fileCache.Memory.Slice(0, indexSize));
            timestampTable = new RegionFileHeaderTable(fileCache.Memory.Slice(indexSize, indexSize));
        }

        public DateTimeOffset GetChunkTimestamp(Vector relativeChunkLocation) => DateTimeOffset.FromUnixTimeSeconds(timestampTable.GetTimestampAtLocation(GetChunkTableLocation(relativeChunkLocation)));
      
        public byte[] GetChunkCompressedBytes(Vector relativeChunkLocation)
        {
            var chunkIndex = GetChunkTableLocation(relativeChunkLocation);
            var (offset, size) = locationTable.GetOffsetSizeAtLocation(chunkIndex);
            Memory<byte> chunkBytes = fileCache.Memory.Slice(offset, size);

            // First 5 bytes are a header.
            // First 4 are filesize.
            // Last is compression scheme. We're always going to use gzip and probably just ignore this.
            var filesize = BitConverter.ToInt32(chunkBytes.Slice(0, 4).ToArray());
            // var compression = (int)chunkBytes.Span[4];
            var nbtBytes = chunkBytes.Slice(5, filesize).ToArray();
            return nbtBytes;
        }

        public void SetChunkCompressedBytes(Vector relativeChunkLocation, byte[] compressedNbtBytes)
        {
            var chunkIndex = GetChunkTableLocation(relativeChunkLocation);
            var (currentOffset, currentSize) = locationTable.GetOffsetSizeAtLocation(chunkIndex);
            var newSize = compressedNbtBytes.Length;
            if (newSize <= currentSize && currentSize > 0)
            {
                // No need to resize the region file. New chunk will fit in place of the old one.
                var chunkBytes = fileCache.Memory.Slice(currentOffset, currentSize);

                // Update the chunk header with the new file size.
                BitConverter.GetBytes(newSize).CopyTo(chunkBytes.Slice(0, 4));
                compressedNbtBytes.CopyTo(chunkBytes.Slice(5, compressedNbtBytes.Length));

                var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
                timestampTable.SetTimestampAtLocation(chunkIndex, timestamp);
                return;
            }

            // New chunk doesn't fit within the previous 4096 boundary that it had, or it doesn't exist.
            // Need to recreate the whole damn file now.

        }




        private int GetChunkTableLocation(Vector relativeChunkLoc) => ((relativeChunkLoc.X % cubicRegionSize) + (relativeChunkLoc.Z % cubicRegionSize * cubicRegionSize)) * 4;

        private async Task InitializeNewFileAsync()
        {
            using FileStream fs = new FileStream(filePath, FileMode.CreateNew);
            var initArray = new Memory<byte>(new byte[indexSize * 2]);
            await fs.WriteAsync(initArray);
            fs.Seek(0, SeekOrigin.Begin);
        }

        private class RegionFileHeaderTable
        {
            private readonly Memory<byte> tableBytes;
            public RegionFileHeaderTable(Memory<byte> rgn)
            {
                tableBytes = rgn;
            }

            public (int offset, int size) GetOffsetSizeAtLocation(int location)
            {
                // First 3 bytes are offset
                var offset = BitConverter.ToInt32(tableBytes.Slice(location, 3).ToArray());
                // Fourth byte is size
                var size = (int)tableBytes.Span[location + 3];
                return (offset << 12, size << 12);
            }

            public void SetOffsetSizeAtLocation(int location, int offset, int size)
            {
                byte[] offsetBytes = BitConverter.GetBytes(offset >> 12);
                offsetBytes.CopyTo(tableBytes.Slice(location, 3).Span);

                // Add one to the size to effectively round up should the size not land on a 4096 boundary.
                tableBytes.Span[location + 3] = (byte) ((size >> 12) + 1);
            }

            public long GetTimestampAtLocation(int location) => (long)BitConverter.ToUInt64(tableBytes.Slice(location, 4).ToArray());

            public void SetTimestampAtLocation(int location, long timestamp) => BitConverter.GetBytes(timestamp).CopyTo(tableBytes.Slice(location, 4).Span);
            
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    regionFileStream.Flush();
                    fileCache.Dispose();
                }

                // TODO: free unmanaged resources (unmanaged objects) and override finalizer
                // TODO: set large fields to null
                disposedValue = true;
            }
        }

        // // TODO: override finalizer only if 'Dispose(bool disposing)' has code to free unmanaged resources
        // ~RegionFile()
        // {
        //     // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
        //     Dispose(disposing: false);
        // }

        public void Dispose()
        {
            // Do not change this code. Put cleanup code in 'Dispose(bool disposing)' method
            Dispose(disposing: true);
            GC.SuppressFinalize(this);
        }
        #endregion IDisposable
    }
}
