namespace Obsidian.Nbt
{
    public partial class NbtTag
    {
        public static NbtTag ToByte(string name, byte value) => new(NbtTagType.Byte, name, value);

        public static NbtTag ToInt(string name, int value) => new(NbtTagType.Int, name, value);

        public static NbtTag ToDouble(string name, double value) => new(NbtTagType.Double, name, value);

        public static NbtTag ToFloat(string name, float value) => new(NbtTagType.Float, name, value);

        public static NbtTag ToShort(string name, short value) => new(NbtTagType.Short, name, value);

        public static NbtTag ToString(string name, string value) => new(NbtTagType.String, name, value);
    }
}
