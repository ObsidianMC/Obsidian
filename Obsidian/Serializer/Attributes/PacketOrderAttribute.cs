using System;

namespace Obsidian.Serializer.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class PacketOrderAttribute : Attribute
    {
        public int Order { get; }

        public bool Absolute { get; }

        public bool CountLength { get; }

        public DataTypes? DataType { get; }

        public PacketOrderAttribute(int order, bool absolute = false, bool countLength = false)
        {
            this.Order = order;
            this.CountLength = countLength;
        }

        public PacketOrderAttribute(int order, DataTypes dataType, bool countLength = false)
        {
            this.Order = order;
            this.DataType = dataType;
            this.CountLength = countLength;
        }
    }

    public enum DataTypes
    {
        VarLong,

        VarInt
    }
}
