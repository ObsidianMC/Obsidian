using Obsidian.API;
using System;
using System.Buffers;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class RegionFile : IDisposable
    {
        private readonly string filePath;
        private readonly int cubicRegionSize;
        private readonly int tableSize;
        private FileStream regionFileStream;
        private RegionFileHeaderTable locationTable, timestampTable;
        private bool disposedValue;
        private readonly IMemoryOwner<byte> fileCache;
        private int nextAvailableOffset;

        /// <summary>
        /// Reference Material: https://wiki.vg/Region_Files#Structure
        /// </summary>
        public RegionFile(string filePath, int cubicRegionSize = 32) 
        {
            this.filePath = filePath;
            this.cubicRegionSize = cubicRegionSize;
            this.tableSize = cubicRegionSize * cubicRegionSize * 4;
            var minCacheSize = (tableSize * 2) + (cubicRegionSize * cubicRegionSize * (4096 + 4));

            fileCache = MemoryPool<byte>.Shared.Rent(minCacheSize);
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

            locationTable = new RegionFileHeaderTable(fileCache.Memory.Slice(0, tableSize));
            timestampTable = new RegionFileHeaderTable(fileCache.Memory.Slice(tableSize, tableSize));

            // Determine end of all allocations/next available allocation
            nextAvailableOffset = 8192;
            for (int x = 0; x < cubicRegionSize; x++)
            {
                for (int z = 0; z < cubicRegionSize; z++)
                {
                    var tableIndex = GetChunkTableIndex(new Vector(x, 0, z));
                    var (offset, size) = locationTable.GetOffsetSizeAtIndex(tableIndex);
                    if (size == 0) { continue; }
                    if (offset + size > nextAvailableOffset)
                    {
                        nextAvailableOffset = offset + size;
                    }
                }
            }
        }

        public async Task FlushToDiskAsync()
        {
            regionFileStream.Seek(0, SeekOrigin.Begin);
            await regionFileStream.WriteAsync(fileCache.Memory);
            await regionFileStream.FlushAsync();
            regionFileStream.Seek(0, SeekOrigin.Begin);
        }

        public DateTimeOffset GetChunkTimestamp(Vector relativeChunkLocation) => DateTimeOffset.FromUnixTimeSeconds(timestampTable.GetTimestampAtLocation(GetChunkTableIndex(relativeChunkLocation)));
      
        public byte[] GetChunkCompressedBytes(Vector relativeChunkLocation)
        {
            // Sanity check
            if (locationTable is null || timestampTable is null)
            {
                return null;
            }
            var chunkIndex = GetChunkTableIndex(relativeChunkLocation);
            var (offset, size) = locationTable.GetOffsetSizeAtIndex(chunkIndex);
            if (size == 0) { return null; }
            Memory<byte> chunkBytes = fileCache.Memory.Slice(offset, size);
            var chunkAllocation = new ChunkAllocation(chunkBytes);

            return chunkAllocation.GetChunkBytes();
        }

        public void SetChunkCompressedBytes(Vector relativeChunkLocation, byte[] compressedNbtBytes)
        {
            // Sanity check
            if (locationTable is null || timestampTable is null)
            {
                return;
            }
            var tableIndex = GetChunkTableIndex(relativeChunkLocation);
            var (currentOffset, currentSize) = locationTable.GetOffsetSizeAtIndex(tableIndex);
            var newSize = compressedNbtBytes.Length;
            Memory<byte> memAllocation;
            if (newSize <= currentSize && currentSize > 0)
            {
                // New chunk will fit in place of the old one.
                memAllocation = GetAllocation(tableIndex);
            }
            else
            {
                memAllocation = GetNewAllocation(newSize, tableIndex);
            }

            var allocation = new ChunkAllocation(memAllocation);
            allocation.SetChunkBytes(compressedNbtBytes);

            var timestamp = DateTimeOffset.Now.ToUnixTimeSeconds();
            timestampTable.SetTimestampAtLocation(tableIndex, timestamp);
        }

        private int GetChunkTableIndex(Vector relativeChunkLoc) => ((relativeChunkLoc.X % cubicRegionSize) + (relativeChunkLoc.Z % cubicRegionSize * cubicRegionSize)) * 4;

        private async Task InitializeNewFileAsync()
        {
            using FileStream fs = new FileStream(filePath, FileMode.CreateNew);
            var initArray = new Memory<byte>(new byte[tableSize * 2]);
            await fs.WriteAsync(initArray);
            fs.Seek(0, SeekOrigin.Begin);
        }

        private Memory<byte> GetAllocation(int tableIndex)
        {
            var (offset, size) = locationTable.GetOffsetSizeAtIndex(tableIndex);
            return fileCache.Memory.Slice(offset, size);
        }

        private Memory<byte> GetNewAllocation(int payloadSize, int tableIndex)
        {
            lock(this)
            {
                var allocationSize = ChunkAllocation.GetAllocationSize(payloadSize);
                var assignedOffset = nextAvailableOffset;
                Interlocked.Add(ref nextAvailableOffset, allocationSize);
                locationTable.SetOffsetSizeAtIndex(tableIndex, assignedOffset, allocationSize);
                return fileCache.Memory.Slice(assignedOffset, allocationSize);
            }
        }

        /// <summary>
        /// Chunks are stored in 4k byte allocations.
        /// Chunk allocations have a 5 byte header.
        /// Then the rest of the payload is compressed nbt.
        /// The header is 4 bytes of length in bytes, and one
        /// byte for compression scheme. 
        /// </summary>
        private class ChunkAllocation
        {
            private readonly Memory<byte> memAllocation;
            private readonly byte compression;
            private int blobSize = 0;

            public ChunkAllocation(Memory<byte> memAllocation, byte compression = 0x1)
            {
                this.memAllocation = memAllocation;
                this.compression = compression;
             }

            public static int GetAllocationSize(double payloadSize)
            {
                // Add the bytes for the header.
                return (int)Math.Ceiling((payloadSize + 5) / 4096.0) << 12;
            }

            public void SetChunkBytes(byte[] compressedNbtBytes)
            {
                blobSize = compressedNbtBytes.Length;
                var blobSizeBytes = BitConverter.GetBytes(blobSize);
                blobSizeBytes.CopyTo(memAllocation.Slice(0, 4).Span);
                memAllocation.Span[4] = compression;
                compressedNbtBytes.CopyTo(memAllocation.Slice(5, blobSize));
            }

            public byte[] GetChunkBytes()
            {
                // First 5 bytes are a header.
                // First 4 are filesize.
                // Fifth is compression scheme. We're always going to use gzip and probably just ignore this.
                var filesize = BitConverter.ToInt32(memAllocation.Slice(0, 4).ToArray());
                // var compression = (int)chunkBytes.Span[4];
                return memAllocation.Slice(5, filesize).ToArray();
            }
        }

        private class RegionFileHeaderTable
        {
            private readonly Memory<byte> tableBytes;
            
            public RegionFileHeaderTable(Memory<byte> tblBytes)
            {
                tableBytes = tblBytes;

            }

            public (int offset, int size) GetOffsetSizeAtIndex(int tableIndex)
            {
                // Fourth byte is size
                var size = (int)tableBytes.Span[tableIndex + 3];
                if (size == 0) { return (0, 0); }
                // First 3 bytes are offset
                Span<byte> bytes = new byte[4];
                tableBytes.Slice(tableIndex, 3).Span.CopyTo(bytes);
                var offset = BitConverter.ToInt32(bytes);
                return (offset << 12, size << 12);
            }

            public void SetOffsetSizeAtIndex(int tableIndex, int offset, int size)
            {
                byte[] offsetBytes = BitConverter.GetBytes(offset >> 12);
                offsetBytes = offsetBytes.Take(3).ToArray();
                offsetBytes.CopyTo(tableBytes.Slice(tableIndex, 3).Span);
                tableBytes.Span[tableIndex + 3] = (byte) (size >> 12);
            }

            public long GetTimestampAtLocation(int location) => (long)BitConverter.ToUInt64(tableBytes.Slice(location, 4).Span);

            public void SetTimestampAtLocation(int location, long timestamp) => BitConverter.GetBytes(timestamp).Take(4).ToArray().CopyTo(tableBytes.Slice(location, 4).Span);
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    FlushToDiskAsync();
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
