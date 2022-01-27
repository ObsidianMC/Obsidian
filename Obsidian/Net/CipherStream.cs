// Original Source:	https://github.com/bcgit/bc-csharp/blob/master/crypto/src/crypto/io/CipherStream.cs
// License:			https://github.com/bcgit/bc-csharp/blob/master/crypto/License.html

using Org.BouncyCastle.Crypto;
using System.Diagnostics;
using System.IO;
using System.Threading;

namespace Obsidian.BouncyCastle;

public class CipherStream : Stream
{
    internal Stream stream;

    public IBufferedCipher ReadCipher { get; }
    public IBufferedCipher WriteCipher { get; }

    private byte[] mInBuf;
    private int mInPos;
    private bool inStreamEnded;
    public override bool CanRead => stream.CanRead && (ReadCipher != null);

    public override bool CanWrite => stream.CanWrite && (WriteCipher != null);


    public CipherStream(Stream stream, IBufferedCipher readCipher, IBufferedCipher writeCipher)
    {
        this.stream = stream;

        if (readCipher != null)
        {
            ReadCipher = readCipher;
            mInBuf = null;
        }

        if (writeCipher != null)
        {
            WriteCipher = writeCipher;
        }
    }
    #region Synchronous
    public override int ReadByte()
    {
        if (ReadCipher == null)
            return stream.ReadByte();

        if (mInBuf == null || mInPos >= mInBuf.Length)
        {
            if (!FillInBuf())
                return -1;
        }

        return mInBuf[mInPos++];
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (ReadCipher == null)
            return stream.Read(buffer, offset, count);

        int num = 0;
        while (num < count)
        {
            if (mInBuf == null || mInPos >= mInBuf.Length)
            {
                if (!FillInBuf())
                    break;
            }

            int numToCopy = Math.Min(count - num, mInBuf.Length - mInPos);
            Array.Copy(mInBuf, mInPos, buffer, offset + num, numToCopy);
            mInPos += numToCopy;
            num += numToCopy;
        }

        return num;
    }


    private bool FillInBuf()
    {
        if (inStreamEnded)
            return false;

        mInPos = 0;

        do
        {
            mInBuf = ReadAndProcessBlock();
        }
        while (!inStreamEnded && mInBuf == null);

        return mInBuf != null;
    }

    private byte[] ReadAndProcessBlock()
    {
        int blockSize = ReadCipher.GetBlockSize();
        int readSize = (blockSize == 0) ? 256 : blockSize;

        byte[] block = new byte[readSize];
        int numRead = 0;
        do
        {
            int count = stream.Read(block, numRead, block.Length - numRead);
            if (count < 1)
            {
                inStreamEnded = true;
                break;
            }
            numRead += count;
        }
        while (numRead < block.Length);

        Debug.Assert(inStreamEnded || numRead == block.Length);

        byte[] bytes = inStreamEnded
            ? ReadCipher.DoFinal(block, 0, numRead)
            : ReadCipher.ProcessBytes(block);

        if (bytes != null && bytes.Length == 0)
        {
            bytes = null;
        }

        return bytes;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Debug.Assert(buffer != null);
        Debug.Assert(0 <= offset && offset <= buffer.Length);
        Debug.Assert(count >= 0);

        int end = offset + count;

        Debug.Assert(0 <= end && end <= buffer.Length);

        if (WriteCipher == null)
        {
            stream.Write(buffer, offset, count);
            return;
        }

        byte[] data = WriteCipher.ProcessBytes(buffer, offset, count);
        if (data != null)
        {
            stream.Write(data, 0, data.Length);
        }
    }

    public override void WriteByte(byte b)
    {
        if (WriteCipher == null)
        {
            stream.WriteByte(b);
            return;
        }

        byte[] data = WriteCipher.ProcessByte(b);
        if (data != null)
        {
            stream.Write(data, 0, data.Length);
        }
    }



    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (WriteCipher != null)
            {
                byte[] data = WriteCipher.DoFinal();
                stream.Write(data, 0, data.Length);
                stream.Flush();
            }
            stream.Dispose();
        }
        base.Dispose(disposing);
    }

    // Note: WriteCipher.DoFinal is only called during Close()
    public override void Flush() => stream.Flush();

    #endregion
    #region Asynchronous

    private async Task<bool> FillInBufAsync()
    {
        if (inStreamEnded)
            return false;

        mInPos = 0;

        do
        {
            mInBuf = await ReadAndProcessBlockAsync();
        }
        while (!inStreamEnded && mInBuf == null);

        return mInBuf != null;
    }

    private async Task<byte[]> ReadAndProcessBlockAsync()
    {
        int blockSize = ReadCipher.GetBlockSize();
        int readSize = (blockSize == 0) ? 256 : blockSize;

        byte[] block = new byte[readSize];
        int numRead = 0;
        do
        {
            int count = await stream.ReadAsync(block, numRead, block.Length - numRead);
            if (count < 1)
            {
                inStreamEnded = true;
                break;
            }
            numRead += count;
        }
        while (numRead < block.Length);

        Debug.Assert(inStreamEnded || numRead == block.Length);

        byte[] bytes = inStreamEnded
            ? ReadCipher.DoFinal(block, 0, numRead)
            : ReadCipher.ProcessBytes(block);

        if (bytes != null && bytes.Length == 0)
        {
            bytes = null;
        }

        return bytes;
    }

    public override Task FlushAsync(CancellationToken cancellationToken) => stream.FlushAsync();

    public override async ValueTask DisposeAsync()
    {
        if (WriteCipher != null)
        {
            byte[] data = WriteCipher.DoFinal();
            await stream.WriteAsync(data, 0, data.Length);
            await stream.FlushAsync();
        }
        await stream.DisposeAsync();
        await base.DisposeAsync();
    }

    public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        if (ReadCipher == null)
            return await stream.ReadAsync(buffer, offset, count);

        int num = 0;
        while (num < count)
        {
            if (mInBuf == null || mInPos >= mInBuf.Length)
            {
                if (!await FillInBufAsync())
                    break;
            }

            if (cancellationToken.IsCancellationRequested)
                break;

            int numToCopy = Math.Min(count - num, mInBuf.Length - mInPos);
            Array.Copy(mInBuf, mInPos, buffer, offset + num, numToCopy);
            mInPos += numToCopy;
            num += numToCopy;
        }

        return num;
    }


    public override async Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        Debug.Assert(buffer != null);
        Debug.Assert(0 <= offset && offset <= buffer.Length);
        Debug.Assert(count >= 0);

        int end = offset + count;

        Debug.Assert(0 <= end && end <= buffer.Length);

        if (WriteCipher == null)
        {
            await stream.WriteAsync(buffer, offset, count);
            return;
        }

        byte[] data = WriteCipher.ProcessBytes(buffer, offset, count);
        if (data != null)
        {
            await stream.WriteAsync(data, 0, data.Length);
        }
    }

    #endregion
    #region Unimplemented & Unsupported
    public override bool CanSeek => false;

    public sealed override long Length => throw new NotSupportedException();

    public sealed override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public sealed override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public sealed override void SetLength(long length) => throw new NotSupportedException();
    #endregion
}
