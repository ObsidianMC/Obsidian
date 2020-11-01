using Obsidian.API.Plugins.Services.IO;
using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Plugins.Services
{
    public abstract class StreamService : IStream
    {
        public bool CanRead => stream.CanRead;

        public bool CanWrite => stream.CanWrite;

        public string Name => name;

        protected Stream stream;
        protected string name;

        public void Close()
        {
            stream.Close();
        }

        public void CopyTo(IStream destination)
        {
            stream.CopyTo((destination as StreamService).stream);
        }

        public Task CopyToAsync(IStream destination)
        {
            return stream.CopyToAsync((destination as StreamService).stream);
        }

        public virtual void Dispose()
        {
            stream.Dispose();
        }

        public virtual ValueTask DisposeAsync()
        {
            return stream.DisposeAsync();
        }

        public virtual void Flush()
        {
            stream.Flush();
        }

        public virtual Task FlushAsync()
        {
            return stream.FlushAsync();
        }

        public int Read(byte[] buffer, int offset, int count)
        {
            return stream.Read(buffer, offset, count);
        }

        public Task<int> ReadAsync(byte[] buffer, int offset, int count)
        {
            return stream.ReadAsync(buffer, offset, count);
        }

        public byte ReadByte()
        {
            Span<byte> value = stackalloc byte[0];
            stream.Read(value);
            return value[0];
        }

        public virtual string ReadLine()
        {
            throw new NotImplementedException();
        }

        public virtual Task<string> ReadLineAsync()
        {
            throw new NotImplementedException();
        }

        public virtual string ReadToEnd()
        {
            throw new NotImplementedException();
        }

        public virtual Task<string> ReadToEndAsync()
        {
            throw new NotImplementedException();
        }

        public virtual void Write(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public virtual void Write(string value)
        {
            throw new NotImplementedException();
        }

        public virtual void Write(object value)
        {
            throw new NotImplementedException();
        }

        public virtual Task WriteAsync(byte[] buffer, int offset, int count)
        {
            throw new NotImplementedException();
        }

        public virtual Task WriteAsync(string value)
        {
            throw new NotImplementedException();
        }

        public virtual Task WriteAsync(object value)
        {
            throw new NotImplementedException();
        }

        public virtual void WriteByte(byte value)
        {
            throw new NotImplementedException();
        }

        public virtual void WriteLine(string value)
        {
            throw new NotImplementedException();
        }

        public virtual void WriteLine(object value)
        {
            throw new NotImplementedException();
        }

        public virtual Task WriteLineAsync(string value)
        {
            throw new NotImplementedException();
        }

        public virtual Task WriteLineAsync(object value)
        {
            throw new NotImplementedException();
        }
    }
}
