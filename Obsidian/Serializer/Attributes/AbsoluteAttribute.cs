using System;

namespace Obsidian.Serializer.Attributes
{
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
    public class AbsoluteAttribute : Attribute
    {
    }
}
