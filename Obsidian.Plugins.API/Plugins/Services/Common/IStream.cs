using System;
using System.Threading.Tasks;

namespace Obsidian.API.Plugins.Services.Common
{
    /// <summary>
    /// Provides a generic view of a sequence of bytes.
    /// </summary>
    public interface IStream : IDisposable, IAsyncDisposable
    {
        /// <summary>
        /// Gets a value that indicates whether the current stream supports reading.
        /// </summary>
        public bool CanRead { get; }

        /// <summary>
        /// Gets a value that indicates whether the current stream supports writing.
        /// </summary>
        public bool CanWrite { get; }

        /// <summary>
        /// Gets the absolute path of the data opened in the <see cref="IStream"/>.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Closes the current stream and releases any resources (such as sockets and file handles) associated with the current stream.
        /// Instead of calling this method, ensure that the stream is properly disposed.
        /// </summary>
        public void Close();

        /// <summary>
        /// Reads the bytes from the current stream and writes them to another stream.
        /// </summary>
        public void CopyTo(IStream stream);

        /// <summary>
        /// Asynchronously reads the bytes from the current stream and writes them to another stream.
        /// </summary>
        public Task CopyToAsync(IStream stream);

        /// <summary>
        /// Clears buffers for this stream and causes any buffered data to be written.
        /// </summary>
        public void Flush();

        /// <summary>
        /// Asynchronously clears all buffers for this stream and causes any buffered data to be written to the underlying device.
        /// </summary>
        public Task FlushAsync();

        /// <summary>
        /// Reads a byte from the stream and advances the read position one byte.
        /// </summary>
        public byte ReadByte();

        /// <summary>
        /// Reads a block of bytes from the stream and writes the data in a given buffer.
        /// </summary>
        public int Read(byte[] buffer, int offset, int count);

        /// <summary>
        /// Asynchronously reads a sequence of bytes from the current stream and writes the data in a given buffer.
        /// </summary>
        public Task<int> ReadAsync(byte[] buffer, int offset, int count);

        /// <summary>
        /// Reads a line of characters from the current stream and returns the data as a string.
        /// </summary>
        public string ReadLine();

        /// <summary>
        /// Reads a line of characters asynchronously from the current stream and returns the data as a string.
        /// </summary>
        public Task<string> ReadLineAsync();

        /// <summary>
        /// Reads all characters from the current position to the end of the stream.
        /// </summary>
        public string ReadToEnd();

        /// <summary>
        /// Reads all characters from the current position to the end of the stream asynchronously and returns them as one string.
        /// </summary>
        public Task<string> ReadToEndAsync();

        /// <summary>
        /// Writes a byte to the current position in the stream.
        /// </summary>
        public void WriteByte(byte value);

        /// <summary>
        /// Writes a block of bytes to the stream.
        /// </summary>
        public void Write(byte[] buffer, int offset, int count);

        /// <summary>
        /// Asynchronously writes a sequence of bytes to the current stream and advances the current position.
        /// </summary>
        public Task WriteAsync(byte[] buffer, int offset, int count);

        /// <summary>
        /// Writes a string to the stream.
        /// </summary>
        public void Write(string value);

        /// <summary>
        /// Asynchronously writes a string to the stream.
        /// </summary>
        public Task WriteAsync(string value);

        /// <summary>
        /// Writes the text representation of an object to the stream.
        /// </summary>
        public void Write(object value);

        /// <summary>
        /// Asynchronously writes the text representation of an object to the stream.
        /// </summary>
        public Task WriteAsync(object value);

        /// <summary>
        /// Writes a string to the stream, followed by a line terminator.
        /// </summary>
        public void WriteLine(string value);

        /// <summary>
        /// Asynchronously writes a string to the stream, followed by a line terminator.
        /// </summary>
        public Task WriteLineAsync(string value);

        /// <summary>
        /// Writes the text representation of an object to the stream, followed by a line terminator.
        /// </summary>
        public void WriteLine(object value);

        /// <summary>
        /// Asynchronously writes the text representation of an object to the stream, followed by a line terminator.
        /// </summary>
        public Task WriteLineAsync(object value);
    }
}
