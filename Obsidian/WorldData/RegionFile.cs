using Obsidian.API;
using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Obsidian.WorldData
{
    public class RegionFile : IDisposable
    {
        private readonly string filePath;
        private readonly int cubicRegionSize;
        private readonly int indexSize;
        private readonly FileStream regionFileStream;
        private RegionFileHeaderTable locationTable, timestampTable;
        private bool disposedValue;
        private readonly IMemoryOwner<byte> fileCache;

        public RegionFile(string filePath, int cubicRegionSize = 32) 
        {
            this.filePath = filePath;
            this.cubicRegionSize = cubicRegionSize;
            this.indexSize = cubicRegionSize * 4;
            regionFileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
            fileCache = MemoryPool<byte>.Shared.Rent();
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(filePath))
            {
                await InitializeNewFileAsync();
            }

            // Load file into memory
            await regionFileStream.ReadAsync(fileCache.Memory);
            regionFileStream.Seek(0, SeekOrigin.Begin);

            locationTable = new RegionFileHeaderTable(fileCache.Memory.Slice(0, indexSize));
            timestampTable = new RegionFileHeaderTable(fileCache.Memory.Slice(indexSize, indexSize));
        }

        private int GetChunkTableLocation(Vector relativeChunkCoord) => ((relativeChunkCoord.X % cubicRegionSize) + (relativeChunkCoord.Z % cubicRegionSize * cubicRegionSize)) * 4;

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
                byte[] offsetBytes = BitConverter.GetBytes(offset >> 12).ToArray();
                offsetBytes.CopyTo(tableBytes.Slice(location, 3).Span);

                // Add one to the size to effectively round up should the size not land on a 4096 boundary.
                tableBytes.Span[location + 3] = (byte) ((size >> 12) + 1);
            }

            public int GetTimestampAtLocation(int location) => BitConverter.ToInt32(tableBytes.Slice(location, 4).ToArray());

            public void SetTimestampAtLocation(int location, int timestamp) => BitConverter.GetBytes(timestamp).ToArray().CopyTo(tableBytes.Slice(location, 4).Span);
            
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
