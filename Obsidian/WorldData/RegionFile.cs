using Obsidian.API;
using System;
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

        public RegionFile(string filePath, int cubicRegionSize = 32) 
        {
            this.filePath = filePath;
            this.cubicRegionSize = cubicRegionSize;
            this.indexSize = cubicRegionSize * 4;
            regionFileStream = new FileStream(filePath, FileMode.Open, FileAccess.ReadWrite);
        }

        public async Task InitializeAsync()
        {
            if (!File.Exists(filePath))
            {
                await InitializeNewFileAsync();
            }

            locationTable = new RegionFileHeaderTable(regionFileStream, indexSize, 0);
            timestampTable = new RegionFileHeaderTable(regionFileStream, indexSize, 1);
            await locationTable.InitAsync();
            await timestampTable.InitAsync();
        }

        private int GetChunkTableIndex(Vector relativeChunkCoord) => (relativeChunkCoord.X % cubicRegionSize) + (relativeChunkCoord.Z % cubicRegionSize * cubicRegionSize);

        private async Task InitializeNewFileAsync()
        {
            using FileStream fs = new FileStream(filePath, FileMode.CreateNew);
            var initArray = new Memory<byte>(new byte[indexSize * 2]);
            await fs.WriteAsync(initArray);
            fs.Seek(0, SeekOrigin.Begin);
        }

        private class RegionFileHeaderTable
        {
            public Memory<byte> bytes;
            private readonly int indexSize;
            private readonly int origin;
            private readonly FileStream fs;
            public RegionFileHeaderTable(FileStream fileStream, int indexSize, int index)
            {
                this.indexSize = indexSize;
                this.origin = index * indexSize;
                fs = fileStream;
                bytes = new byte[indexSize];
            }

            public async Task InitAsync()
            {
                fs.Seek(origin, SeekOrigin.Begin);
                await fs.ReadAsync(bytes);
                fs.Seek(0, SeekOrigin.Begin);
            }
        }

        #region IDisposable
        protected virtual void Dispose(bool disposing)
        {
            if (!disposedValue)
            {
                if (disposing)
                {
                    regionFileStream.Flush();
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
