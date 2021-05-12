using System;
using System.IO;
using System.Threading.Tasks;

namespace Obsidian.Nbt
{
    public sealed partial class NbtWriter : IDisposable, IAsyncDisposable
    {
        private NbtTagType? expectedListType;

        private int listSize;
        private int listIndex;

        public NbtTagType RootType { get; }

        public bool IsClosed { get; internal set; }

        public Stream BaseStream { get; }

        public NbtWriter(Stream outstream, NbtTagType rootType, string name)
        {
            this.BaseStream = outstream;
            this.RootType = rootType;

            this.Write(rootType);
            this.Write(name);
        }

        public void WriteCompoundStart(string name = "")
        {
            this.Validate(name, NbtTagType.Compound);

            this.Write(NbtTagType.Compound);
            if (!string.IsNullOrEmpty(name))
                this.Write(name);
        }

        public void WriteListStart(string name, NbtTagType listType, int length)
        {
            this.Validate(name, NbtTagType.List);

            this.listSize = length;
            this.expectedListType = listType;

            this.Write(NbtTagType.List);
            if (!string.IsNullOrEmpty(name))
                this.Write(name);

            this.Write(listType);
            this.Write(length);
        }

        public void EndList()
        {
            if ((this.listIndex + 1) < this.listSize)
                throw new InvalidOperationException("List cannot end because its size is smaller than the pre-defined size.");

            this.listSize = 0;
            this.listIndex = 0;
            this.expectedListType = null;
        }

        public void EndCompound()
        {
            this.Write(NbtTagType.End);
        }

        public void WriteString(string name, string value)
        {
            this.Validate(name, NbtTagType.String);

            this.Write(NbtTagType.String);
            this.Write(name);
            this.Write(value);
        }

        public void WriteByte(string name, byte value)
        {
            this.Validate(name, NbtTagType.Byte);

            this.Write(NbtTagType.Byte);
            this.Write(name);
            this.Write(value);
        }

        public void WriteShort(string name, short value)
        {
            this.Validate(name, NbtTagType.Short);

            this.Write(NbtTagType.Short);
            this.Write(name);
            this.Write(value);
        }

        public void WriteInt(string name, int value)
        {
            this.Validate(name, NbtTagType.Int);

            this.Write(NbtTagType.Int);
            this.Write(name);
            this.Write(value);
        }

        public void WriteLong(string name, long value)
        {
            this.Validate(name, NbtTagType.Long);

            this.Write(NbtTagType.Long);
            this.Write(name);
            this.Write(value);
        }

        public void WriteFloat(string name, float value)
        {
            this.Validate(name, NbtTagType.Float);

            this.Write(NbtTagType.Float);
            this.Write(name);
            this.Write(value);
        }

        public void WriteDouble(string name, double value)
        {
            this.Validate(name, NbtTagType.Double);

            this.Write(NbtTagType.Double);
            this.Write(name);
            this.Write(value);
        }

        public void Validate(string name, NbtTagType type)
        {
            if (this.IsClosed)
                throw new InvalidOperationException("Cannot write any more tags. Writer has been closed.");

            if (string.IsNullOrEmpty(name) && this.RootType == NbtTagType.Compound)
                throw new ArgumentException("Tags inside a compound tag must have a name.");

            if (this.RootType == NbtTagType.List)
            {
                if (this.expectedListType != type)
                    throw new InvalidOperationException($"Expected list type: {this.expectedListType}. Got: {type}");
                else if (!string.IsNullOrEmpty(name))
                    throw new InvalidOperationException("Tags inside lists must be nameless.");
                else if ((this.listIndex + 1) > this.listSize)
                    throw new IndexOutOfRangeException("Exceeded pre-defined list size");

                this.listIndex++;
            }
        }

        //TODO
        public void TryFinish()
        {
           
        }

        public ValueTask DisposeAsync() => this.BaseStream.DisposeAsync();
        public void Dispose() => this.BaseStream.Dispose();

        private record TagNode(NbtTagType Type);
    }
}
