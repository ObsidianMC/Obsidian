using System;

namespace Obsidian.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
    public sealed class VectorFormatAttribute : Attribute
    {
        public Type Type { get; }

        public VectorFormatAttribute(Type type)
        {
            Type = type;   
        }
    }
}
