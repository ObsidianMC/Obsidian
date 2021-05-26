using System;

namespace Obsidian.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class ActualTypeAttribute : Attribute
    {
        public ActualTypeAttribute(Type type)
        {
        }
    }
}
