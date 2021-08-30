using System;
using System.Diagnostics;
using System.Text;

namespace Obsidian.API.Performance
{
    [DebuggerDisplay("{" + nameof(ToString) + "()}")]
    public sealed class Utf8Message
    {
        internal readonly byte[][] bytes;
        internal readonly int length;

        public Utf8Message(string message)
        {
            if (message is null)
                throw new ArgumentNullException(nameof(message));

            byte[] messageBytes = Encoding.UTF8.GetBytes(message);
            length = messageBytes.Length;
            bytes = new[] { messageBytes };
        }

        internal Utf8Message(byte[] bytes)
        {
            if (bytes is null)
                throw new ArgumentNullException(nameof(bytes));

            length = bytes.Length;
            this.bytes = new[] { bytes };
        }

        internal Utf8Message(byte[][] bytes)
        {
            if (bytes is null)
                throw new ArgumentNullException(nameof(bytes));

            length = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                length += bytes[i].Length;
            }

            this.bytes = bytes;
        }

        /// <summary>
        /// Returns a string that represents the <see cref="Utf8Message"/>.
        /// </summary>
        /// <returns>A string that represents the <see cref="Utf8Message"/>.</returns>
        public override string ToString()
        {
            byte[] buffer = new byte[length];
            int index = 0;
            for (int i = 0; i < bytes.Length; i++)
            {
                bytes[i].CopyTo(buffer.AsMemory(index));
                index += bytes[i].Length;
            }
            return Encoding.UTF8.GetString(buffer);
        }
    }
}
