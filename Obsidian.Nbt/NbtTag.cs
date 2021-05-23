using System;

namespace Obsidian.Nbt
{
    public struct NbtTag<T> : INbtTag
    {
        public NbtTagType Type { get; }

        public string Name { get; set; }

        /// <summary>
        /// This is either null, a compound or list
        /// </summary>
        public INbtTag Parent { get; set; }

        public T Value { get; }

        public NbtTag(string name, T value, INbtTag parent = null)
        {
            this.Name = name;
            this.Parent = parent;
            this.Value = value;
            this.Type = value switch
            {
                bool => NbtTagType.Byte,
                byte => NbtTagType.Byte,
                short => NbtTagType.Short,
                int => NbtTagType.Int,
                long => NbtTagType.Long,
                float => NbtTagType.Float,
                double => NbtTagType.Double,
                string => NbtTagType.String,
                _ => throw new InvalidOperationException()
            };
        }

        public string PrettyString(int depth = 4)
        {
            var t = $"{this}";
            return t.PadLeft(depth + t.Length);
        }

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
                    return $"TAG_{this.Type}('{this.Name}'): {this.Value}";
                default:
                    throw new NotSupportedException("Only generic types are supported.");
            }
        }
    }

    public interface INbtTag
    {
        public NbtTagType Type { get; }

        public string Name { get; set; }

        public INbtTag Parent { get; set; }

        public string PrettyString(int depth = 4);
    }
}
