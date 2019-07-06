using System;

namespace Obsidian.Util
{
    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public class VariableAttribute : Attribute
    {
        public VariableType Type { get; }

        public int Size { get; }

        public int Order { get; }

        public VariableAttribute(VariableType type = VariableType.Auto, int order = 0, int size = 0)
        {
            this.Type = type;
            this.Size = size;
            this.Order = order;
        }
    }

    public enum VariableType
    {
        /// <summary>
        /// Automatically figures out what data type to use for this variable.
        /// </summary>
        Auto,

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

        Boolean,

        Float,

        Double,

        Transform,

        UUID,

        Chat
    }
}