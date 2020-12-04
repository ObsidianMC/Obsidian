using System;

namespace Obsidian.Serializer.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public class ActualTypeAttribute : Attribute
    {
        public Type Type { get; private set; }

        public ActualTypeAttribute(Type type)
        {
            Type = type;
        }
    }
}
