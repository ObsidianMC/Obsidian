using System;

namespace Obsidian.Serializer.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class WriteMethod : Attribute
    {
    }
}
