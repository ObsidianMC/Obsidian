using System;

namespace Obsidian.Serialization.Attributes
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = true)]
    public class WriteMethod : Attribute
    {
    }
}
