using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Obsidian.Util.Debug
{
    public class PacketDebugStream : Stream
    {
        public Stream Target { get; }

        //TODO: Dispose of this when done
        public MemoryStream MemoryStream { get; } = new MemoryStream();

        public List<PacketDebugMarker> Markers { get; } = new List<PacketDebugMarker>();
        
        public PacketDebugStream(Stream target)
        {
            Target = target;
        }

        public override void Flush()
        {
            Target.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count) => Target.Read(buffer, offset, count);

        public override long Seek(long offset, SeekOrigin origin) => Target.Seek(offset, origin);

        public override void SetLength(long value) => Target.SetLength(value);

        public override void Write(byte[] buffer, int offset, int count)
        {
            MemoryStream.Write(buffer, offset, count);
            Target.Write(buffer, offset, count);
        }

        private void ExportData()
        {
            while (Markers.Any())
            {
                var marker = Markers.First();
                var offset = (int)(marker.StartPosition - this.Position);
                var buffer = new byte[marker.Length];

                MemoryStream.Read(buffer, offset, buffer.Length);
                
                PacketDebug.Append(marker.Name, buffer);
            }
        }

        public override bool CanRead => Target.CanRead;
        public override bool CanSeek => Target.CanSeek;
        public override bool CanWrite  => Target.CanWrite;
        public override long Length => Target.Length;
        public override long Position
        {
            get => Target.Position;
            set => Target.Position = value;
        }
    }
}