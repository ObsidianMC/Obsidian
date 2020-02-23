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
        public MemoryStream MemoryStream { get; set; } = new MemoryStream();

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

            PacketDebug.Append("", MemoryStream.ToArray());
            MemoryStream = new MemoryStream();
            
            
            Target.Write(buffer, offset, count);
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