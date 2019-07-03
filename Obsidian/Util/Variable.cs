using System;

namespace Obsidian.Util
{

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class VariableAttribute : Attribute
    {
        public VariableType Type { get; }

        public int Size { get; }

        public VariableAttribute(VariableType type = VariableType.Unknown, int size = 0)
        {
            this.Type = type;
            this.Size = size;
        }
    }

    public enum VariableType
    {
        Int,

        Long,

        VarInt,

        VarLong,

        UnsignedByte,

        Byte,

        Short,

        UnsignedShort,

        String,

        Array,

        List,

        Position,

        Unknown,
        Boolean,
        Float,
        Double,
        Tranform
    }
}
