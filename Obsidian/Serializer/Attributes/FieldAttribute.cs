using Obsidian.Serializer.Enums;
using System;

namespace Obsidian.Serializer.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class FieldAttribute : Attribute
    {
        public int Order { get; }

        public bool Absolute { get; }

        public bool CountLength { get; set; }

        public DataType Type { get; set; } = DataType.Auto;

        /// <summary>
        /// Max length of characters in a string
        /// </summary>
        public int MaxLength { get; set; }

        public FieldAttribute(int order)
        {
            this.Order = order;
        }

        public FieldAttribute(int order, bool absolute = false)
        {
            this.Order = order;
            this.Absolute = absolute;
        }
    }    
}
