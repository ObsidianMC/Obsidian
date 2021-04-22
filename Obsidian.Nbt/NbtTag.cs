namespace Obsidian.Nbt
{
    public class NbtTag
    {
        public NbtTagType Type { get; }

        public NbtTag Parent { get; set; }

        public string Name { get; internal set; }

        private object value;

        public NbtTag(NbtTagType type)
        {
            this.Type = type;
        }

        public NbtTag(NbtTagType type, string name, object value) : this(type)
        {
            this.Name = name;
            this.value = value;
        }

        public string GetString() => this.value.ToString();

        public byte GetByte() => (byte)this.value;

        public float GetFloat() => (float)this.value;

        public double GetDouble() => (double)this.value;

        public int GetInt() => (int)this.value;

        public long GetLong() => (long)this.value;

        public short GetShort() => (short)this.value;

        public override string ToString()
        {
            switch (this.Type)
            {
                case NbtTagType.Byte:
                case NbtTagType.Short:
                case NbtTagType.Int:
                case NbtTagType.Long:
                case NbtTagType.Float:
                case NbtTagType.Double:
                case NbtTagType.String:
                    return $"TAG_{this.Type}('{this.Name}'): {this.value}";
                case NbtTagType.ByteArray:
                    break;
                case NbtTagType.List:
                    break;
                case NbtTagType.Compound:
                    return ((NbtCompound)this).ToString();
                case NbtTagType.IntArray:
                    break;
                case NbtTagType.LongArray:
                    break;
                default:
                    break;
            }
            return "";
        }
    }
}
