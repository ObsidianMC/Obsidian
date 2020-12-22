using System;

namespace Obsidian.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class FieldAttribute : Attribute
    {
        public int Order { get; }

        public FieldAttribute(int order)
        {
            Order = order;
        }
    }    
}
